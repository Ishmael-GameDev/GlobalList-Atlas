using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Maps;
using GlobalListAtlas.Net;
using GlobalListAtlas.Server;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.UI;

public enum MapStatus
{
    NotDownloaded,
    Installed,
    Running
}

public class MapListPanel : MonoBehaviour
{
    public static MapListPanel Instance { get; private set; }
    internal const float ScreenPadding = 24f;
    internal const float PanelWidthFraction = 1f / 3f;
    internal const float PanelHorizontalShift = 338f;
    private const float AutoScrollMarginFraction = 0.15f;
    private const float HoldRepeatDelaySeconds = 0.5f;
    private const float HoldRepeatRatePerSecond = 8f;
    private const float StatusBarHeight = 22f;
    private const float FilterRowHeight = 40f;
    private const float CatalogProgressBarHeight = 22f;
    private const float ArchitectProgressBarHeight = 10f;
    private const float ArchitectStatusTextHeight = 74f;
    private const float MirrorRowHeight = 96f;
    private const float ArchitectStatusIntervalSeconds = 0.5f;
    private const float SearchRowHeight = 28f;
    private const float ActiveMapsRowHeight = 20f;
    private const float RowGap = 4f;
    private const float FavoritesButtonSize = 30f;
    private const string FavoritesGlyph = "♥";
    private const float TopRowGap = 6f;

    private const float TopReservedHeight =
        StatusBarHeight + RowGap + ActiveMapsRowHeight + RowGap + SearchRowHeight + RowGap +
        FilterRowHeight + RowGap + FavoritesButtonSize + RowGap + CatalogProgressBarHeight + RowGap;
    private const float CatalogRetryIntervalSeconds = 10f;
    private const float StatusBarUpdateIntervalSeconds = 0.5f;

    private int _heldDirection = 0;
    private float _heldDuration = 0f;
    private float _timeSinceLastRepeat = 0f;
    private GameObject _canvasGo;
    private RectTransform _canvasRoot;
    private RectTransform _listContent;
    private ScrollRect _scrollRect;
    private Text _statusBarText;
    private Text _activeMapsText;
    private string _activeMapsLastRaw;
    private float _activeRowHeight = ActiveMapsRowHeight;
    private RectTransform _activeRect, _searchRect, _filterRowRect, _eventFilterRect, _favRect, _progressRect, _listScrollRoot;
    private readonly List<RectTransform> _tabRects = new();
    private float _activeMapsTimer;
    private string _searchQuery = "";
    private bool _favoritesOnly;
    private (Button Button, Image Image) _favoritesButton;
    private float _statusBarTimer;
    private float _archStatusTimer;
    private float _progressTextExtra;
    private DateTime? _retryAt;
    private GameObject _catalogProgressGo;
    private GameObject _mirrorGo;
    private GameObject _vpnGuideGo;
    private GameObject _savedCatalogGo;
    private bool _mirrorOffer;
    private int _loadGen;
    private Image _catalogProgressFill;
    private Text _catalogProgressText;
    private Button _langRuButton;
    private Image _langRuImage;
    private Button _langEnButton;
    private Image _langEnImage;
    private GameObject _overlayGo;

    private readonly List<MapRow> _entries = new();
    private readonly List<GameObject> _rowGos = new();
    private readonly List<GameObject> _buttonGos = new();
    private readonly List<Image> _buttonImages = new();
    private readonly List<int> _displayEntryIndices = new();
    private readonly Dictionary<League, List<GameObject>> _headerGosByLeague = new();

    private readonly Dictionary<string, List<GameObject>> _headerGosByGroup = new();

    public enum ServerSortMode { UploadDate, Downloads }
    private ServerSortMode _serverSort = ServerSortMode.UploadDate;

    private bool _showSilksong;

    private class SetFilter<T>
    {
        public readonly HashSet<T> Selected = new();
        public bool ShowAll = true;
        public void Reset() { Selected.Clear(); ShowAll = true; }
        public bool Passes(T value) => ShowAll || Selected.Contains(value);
    }

    private readonly SetFilter<ArchitectSource> _sourceFilter = new();
    private readonly SetFilter<ServerDifficulty> _difficultyFilter = new();
    private readonly SetFilter<ServerDuration> _durationFilter = new();
    private readonly SetFilter<ServerTag> _serverTagFilter = new();
    private readonly SetFilter<MapStatus> _serverStatusFilter = new();

    private TriFilterMode _seenMode;
    private bool _likedOnly, _dislikedOnly;
    private readonly List<(Image Image, Text Text, Func<TriFilterMode> Mode, Color OnColor, string Glyph)> _markFilterButtons = new();
    private readonly List<RectTransform> _markFilterRects = new();

    private GameObject _serverFilterRow;
    private RectTransform _serverFilterRect;
    private Text _sortButtonText;
    private RectTransform _sortButtonRect;

    private static readonly int[] DownloadThresholds = { 0, 10, 25, 50, 100, 250, 500, 1000 };
    private readonly HashSet<int> _selectedStars = new();
    private bool _starsShowAll = true;
    private readonly HashSet<MapEditor> _selectedEditors = new();
    private bool _editorsShowAll = true;
    private readonly HashSet<bool> _selectedVerification = new();
    private bool _verificationShowAll = true;
    private readonly HashSet<MapTag> _selectedTags = new();
    private bool _tagsShowAll = true;
    private readonly HashSet<MapStatus> _selectedStatuses = new();
    private bool _statusesShowAll = true;

    private readonly HashSet<EventMapType> _selectedEventTypes = new();
    private bool _eventTypesShowAll = true;

    private GameObject _globalFilterRow;
    private GameObject _eventFilterRow;
    private readonly List<(MapCatalogKind Kind, Image Image, Text Text)> _catalogTabs = new();
    private readonly Dictionary<MapCatalogKind, RectTransform> _catalogTabRects = new();
    private (Button Button, Image Image) _devToolsButton;
    private Image _devToolsIcon;
    private RectTransform _reloadRect;
    private List<int> _visibleButtonIndices = new();
    private int _selectedButtonIndex = -1;
    private bool _isOpen;
    private bool _isLoading;
    private string _loadError;
    private bool _retryScheduled;
    private bool _networkSubscribed;
    private bool _progressSubscribed;

    public event Action<MapRow> SelectionChanged;
    public event Action<bool> OpenStateChanged;
    public bool IsOpen => _isOpen;
    public MapRow SelectedMap =>
        (_selectedButtonIndex >= 0 && _selectedButtonIndex < _displayEntryIndices.Count)
            ? _entries[_displayEntryIndices[_selectedButtonIndex]]
            : null;

    private void Awake()
    {
        Instance = this;
        NetworkMonitor.EnsureCreated();
    }
    private enum CacheState { Ok, Missing, Outdated }

    private CacheState GetCacheState()
    {
        string cacheDir = Path.Combine(Application.persistentDataPath, "GlobalListAtlas");
        string cachePath = Path.Combine(cacheDir, "catalog_cache.csv");

        if (!File.Exists(cachePath))
            return CacheState.Missing;

        try
        {
            DateTime lastWrite = File.GetLastWriteTime(cachePath);
            if (DateTime.Now - lastWrite > TimeSpan.FromHours(24))
            {
                return CacheState.Outdated;
            }
        }
        catch
        {
        }

        return CacheState.Ok;
    }
    private static Sprite LoadFlagSprite(string flagName)
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith($"{flagName}.png", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(resourceName)) return null;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                var tex = new Texture2D(1, 1);
                tex.LoadImage(buffer, true);
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
        catch { return null; }
    }

    private void UpdateLanguageButtons()
    {
        if (_langRuButton == null || _langEnButton == null) return;

        string currentLang = GlobalListAtlasMod.Instance?.Settings.CurrentLanguage ?? "en";
        bool isRuActive = currentLang == "ru";

        _langRuImage.color = isRuActive ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        _langEnImage.color = isRuActive ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        _langRuButton.interactable = !isRuActive;
        _langEnButton.interactable = isRuActive;

    }
    private void RefreshAllUI()
    {
        if (_canvasGo != null)
        {
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
        }

        _statusBarText = null;
        _catalogProgressGo = null;
        _catalogProgressFill = null;
        _catalogProgressText = null;
        _langRuButton = null;
        _langRuImage = null;
        _langEnButton = null;
        _langEnImage = null;
        _listContent = null;
        _scrollRect = null;

        BuildCanvasIfNeeded();

        UpdateLanguageButtons();

        if (_isOpen)
        {
            _ = LoadAndPopulateAsync();
        }
    }
    private void RebuildFilterButtons()
    {
        if (_canvasGo != null)
        {
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            BuildCanvasIfNeeded();
            _canvasGo.SetActive(_isOpen);
            _ = LoadAndPopulateAsync();
        }
    }

    private void RebuildLeagueHeaders()
    {
        RebuildButtons();
    }

    private void OnDestroy()
    {
        DestroyOverlay();
        if (_networkSubscribed && NetworkMonitor.Instance != null)
            NetworkMonitor.Instance.OnlineStateChanged -= OnNetworkOnlineStateChanged;
        if (_progressSubscribed && GlobalListAtlasMod.Instance?.DownloadManager != null)
            GlobalListAtlasMod.Instance.DownloadManager.CatalogLoadProgressChanged -= OnCatalogLoadProgressChanged;
    }

    public void Toggle()
    {
        if (_isOpen) Close();
        else Open();
    }

    public void Open()
    {
        BuildCanvasIfNeeded();
        CreateOverlay();
        _canvasGo.SetActive(true);
        _isOpen = true;
        TakeInputFromGameMenu();
        OpenStateChanged?.Invoke(true);
        _ = LoadAndPopulateAsync();
    }

    public void Close()
    {
        PopupStack.CloseAll();

        _isOpen = false;
        if (_canvasGo != null)
            _canvasGo.SetActive(false);
        DestroyOverlay();
        ReturnInputToGameMenu();
        OpenStateChanged?.Invoke(false);
    }

    private GameObject _gameMenuSelection;

    private static readonly string[] OwnCanvasNames =
    {
        "MapListPanelCanvas", "MapDetailsPanelCanvas", "FilterPopupCanvas", "BackupsPopupCanvas", "HelpPopupCanvas"
    };

    private static bool IsOwnUi(GameObject go) =>
        go != null && OwnCanvasNames.Contains(go.transform.root.name);

    private void TakeInputFromGameMenu()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null) return;

        var selected = eventSystem.currentSelectedGameObject;
        if (selected != null && !IsOwnUi(selected))
            _gameMenuSelection = selected;

        eventSystem.SetSelectedGameObject(null);
    }

    private void ReturnInputToGameMenu()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem != null && _gameMenuSelection != null && _gameMenuSelection.activeInHierarchy)
            eventSystem.SetSelectedGameObject(_gameMenuSelection);

        _gameMenuSelection = null;
    }

    private void KeepInputOnPanel()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && !IsOwnUi(selected))
        {
            if (_gameMenuSelection == null) _gameMenuSelection = selected;
            eventSystem.SetSelectedGameObject(null);
        }
    }

    private readonly HashSet<MapCatalogKind> _loadingCatalogs = new();

    private async Task LoadAndPopulateAsync()
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null)
        {
            _loadError = Localization.Get("error.manager_unavailable");
            return;
        }

        var requestedCatalog = manager.CurrentCatalog;
        int gen = _loadGen;
        _isLoading = true;
        SetCatalogProgressVisible(true);
        SetCatalogProgressIndeterminate(Localization.Get("list.loading_catalog"));

        if (_loadingCatalogs.Contains(requestedCatalog))
            return;

        _loadingCatalogs.Add(requestedCatalog);
        try
        {
            var allMaps = await manager.GetAllMapsAsync();

            if (gen != _loadGen || manager.CurrentCatalog != requestedCatalog)
                return;

            _entries.Clear();
            _entries.AddRange(allMaps);
            ApplyArchitectDuplicates(allMaps);
            RebuildButtons();
            RefreshVisibility();
            if (_canvasGo != null)
            {
                _canvasGo.SetActive(_isOpen);
                OpenStateChanged?.Invoke(_isOpen);
            }
            _mirrorOffer = false;
            _loadError = null;
        }
        catch (Exception e)
        {
            if (gen != _loadGen) return;
            if (manager.CurrentCatalog != requestedCatalog)
            {
                Log.Warn($"[Список карт] Фоновая загрузка каталога {requestedCatalog} не удалась: {e.Message}");
            }
            else
            {
                _loadError = e.Message;
                _mirrorOffer = manager.ServerUnreachable && requestedCatalog == MapCatalogKind.ArchitectServer
                    && !ArchitectServerConfig.UseMirror;
                Log.Warn($"[Список карт] Не удалось загрузить каталог: {e.Message}. " +
                                    $"Повтор через {CatalogRetryIntervalSeconds:F0} с...");
            }
        }
        finally
        {
            if (gen == _loadGen) _loadingCatalogs.Remove(requestedCatalog);
            if (gen == _loadGen && manager.CurrentCatalog == requestedCatalog)
            {
                _isLoading = false;
                SetCatalogProgressVisible(_loadError != null);
                if (_loadError != null)
                {
                    SetCatalogProgressError(_loadError);
                    ScheduleRetry();
                }
            }
        }
    }

    private void ScheduleRetry()
    {
        if (_retryScheduled) return;
        _retryScheduled = true;
        _ = RetryLoopAsync();
    }

    private async Task RetryLoopAsync()
    {
        int gen = _loadGen;
        try
        {
            while (_loadError != null && _isOpen
                   && !(CurrentCatalog == MapCatalogKind.ArchitectServer && ArchitectServerConfig.UseMirror))
            {
                _retryAt = DateTime.UtcNow.AddSeconds(CatalogRetryIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(CatalogRetryIntervalSeconds));
                if (!_isOpen || gen != _loadGen) break;
                await LoadAndPopulateAsync();
            }
        }
        finally
        {
            _retryScheduled = false;
            _retryAt = null;
        }
    }

    private void OnNetworkOnlineStateChanged(bool isOnline)
    {
        if (isOnline && _loadError != null && _isOpen && !_isLoading)
            _ = LoadAndPopulateAsync();
    }

    private static void ApplyArchitectDuplicates(List<MapRow> maps)
    {
        foreach (var map in maps) map.HiddenDuplicate = false;
        var architect = maps.Where(m => m.Catalog == MapCatalogKind.ArchitectServer).ToList();
        foreach (var group in DuplicateFinder.FindCandidates(architect))
            DuplicateFinder.HideOlder(group);
    }

    private void OnSavedCatalogClicked()
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null || !manager.HasSavedServerCatalog) return;

        _loadGen++;
        _loadingCatalogs.Clear();
        _mirrorOffer = false;
        manager.LoadSavedServerCatalog();
        _entries.Clear();
        RebuildButtons();
        RefreshVisibility();
        ApplyProgressBarLayout();
        LayoutTopArea();
        _ = LoadAndPopulateAsync();
    }

    private void OnMirrorSwitchClicked()
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return;

        ArchitectServerConfig.UseMirror = true;
        _mirrorOffer = false;
        _loadGen++;
        _loadingCatalogs.Clear();
        manager.CancelServerFetches();
        manager.InvalidateServerCatalog();
        _entries.Clear();
        RebuildButtons();
        RefreshVisibility();
        ApplyProgressBarLayout();
        LayoutTopArea();
        _ = LoadAndPopulateAsync();
    }

    private void OnCatalogLoadProgressChanged(MapCatalogKind catalog, long received, long? total)
    {
        if (!_isLoading || catalog != CurrentCatalog) return;

        if (total.HasValue && total.Value > 0)
        {
            float fraction = Mathf.Clamp01((float)received / total.Value);
            if (_catalogProgressFill != null)
                ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(fraction, 1);
            if (_catalogProgressText != null)
                _catalogProgressText.text = Localization.Get("catalog.loading_percent",
                    fraction * 100f, FormatBytes(received), FormatBytes(total.Value));
        }
        else if (_catalogProgressText != null)
        {
            _catalogProgressText.text = Localization.Get("catalog.loading_bytes", FormatBytes(received));
        }
    }

    private void SetCatalogProgressVisible(bool visible)
    {
        if (_catalogProgressGo != null)
            _catalogProgressGo.SetActive(visible);
        ApplyProgressBarLayout();
        LayoutTopArea();
    }

    private void ApplyProgressBarLayout()
    {
        if (_progressRect == null || _catalogProgressText == null) return;

        bool server = CurrentCatalog == MapCatalogKind.ArchitectServer;
        _progressRect.sizeDelta = new Vector2(0, server ? ArchitectProgressBarHeight : CatalogProgressBarHeight);
        _progressTextExtra = server ? ArchitectStatusTextHeight + (_mirrorOffer ? MirrorRowHeight : 0f) : 0f;
        if (_mirrorGo != null) _mirrorGo.SetActive(server && _mirrorOffer);
        if (_vpnGuideGo != null) _vpnGuideGo.SetActive(server && _mirrorOffer);
        if (_savedCatalogGo != null)
            _savedCatalogGo.SetActive(server && _mirrorOffer && GlobalListAtlasMod.Instance?.DownloadManager?.HasSavedServerCatalog == true);

        var textRect = (RectTransform)_catalogProgressText.transform;
        textRect.anchorMin = server ? new Vector2(0, 1) : Vector2.zero;
        textRect.anchorMax = server ? new Vector2(1, 1) : Vector2.one;
        textRect.pivot = server ? new Vector2(0.5f, 1) : new Vector2(0.5f, 0.5f);
        textRect.offsetMin = server ? new Vector2(4, -(ArchitectProgressBarHeight + 2f + ArchitectStatusTextHeight)) : Vector2.zero;
        textRect.offsetMax = server ? new Vector2(-4, -(ArchitectProgressBarHeight + 2f)) : Vector2.zero;

        _catalogProgressText.alignment = server ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter;
        _catalogProgressText.horizontalOverflow = server ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        _catalogProgressText.verticalOverflow = server ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
        _catalogProgressText.fontSize = server ? 12 : 13;
    }

    private void LayoutTopArea()
    {
        if (_activeRect == null || _listScrollRoot == null) return;

        float y = StatusBarHeight + RowGap;

        _activeRect.sizeDelta = new Vector2(0, _activeRowHeight);
        _activeRect.anchoredPosition = new Vector2(0, -y);
        y += _activeRowHeight + RowGap;

        _searchRect.anchoredPosition = new Vector2(0, -y);
        y += SearchRowHeight + RowGap;

        _filterRowRect.anchoredPosition = new Vector2(0, -y);
        if (_eventFilterRect != null) _eventFilterRect.anchoredPosition = new Vector2(0, -y);
        if (_serverFilterRect != null) _serverFilterRect.anchoredPosition = new Vector2(0, -y);
        y += FilterRowHeight + RowGap;

        _favRect.anchoredPosition = new Vector2(_favRect.anchoredPosition.x, -y);
        foreach (var tab in _tabRects) tab.anchoredPosition = new Vector2(tab.anchoredPosition.x, -y);
        y += FavoritesButtonSize + RowGap;

        if (_catalogProgressGo != null && _catalogProgressGo.activeSelf)
        {
            _progressRect.anchoredPosition = new Vector2(0, -y);
            y += _progressRect.sizeDelta.y + _progressTextExtra + RowGap;
        }

        _listScrollRoot.offsetMax = new Vector2(0, -y);
    }

    private void SetCatalogProgressIndeterminate(string message)
    {
        if (_catalogProgressFill != null)
            ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(0, 1);
        if (_catalogProgressText != null)
            _catalogProgressText.text = message;
    }

    private string BuildArchitectStatusText(string error = null)
    {
        var now = DateTime.UtcNow;
        var snapshot = ArchitectLoadStatus.Snapshot();
        var lines = new List<string>();

        double? longestWait = null;
        bool anyResponse = false;
        foreach (var (_, p) in snapshot)
        {
            if (p.Responses > 0) anyResponse = true;
            if (p.RequestSentAt.HasValue)
                longestWait = Math.Max(longestWait ?? 0, (now - p.RequestSentAt.Value).TotalSeconds);
        }
        bool allFailed = snapshot.Count > 0 && snapshot.All(s => s.Progress.State == ArchitectSourceState.Failed);

        string connection = allFailed ? Localization.Get("arch.status.conn_lost")
            : anyResponse ? Localization.Get("arch.status.conn_ok")
            : longestWait.HasValue ? Localization.Get("arch.status.conn_wait", (int)longestWait.Value)
            : Localization.Get("arch.status.conn_connecting");

        string header = ArchitectLoadStatus.StartedAt.HasValue
            ? Localization.Get("arch.status.elapsed", (int)(now - ArchitectLoadStatus.StartedAt.Value).TotalSeconds) + "  ·  " + connection
            : connection;
        lines.Add(header);

        foreach (var (source, p) in snapshot)
        {
            string name = ArchitectServerConfig.GetSourceLabel(source);
            string line = p.State switch
            {
                ArchitectSourceState.Failed => Localization.Get("arch.status.source_failed", name, p.Error),
                ArchitectSourceState.Done => Localization.Get("arch.status.source_done", name, p.Maps),
                ArchitectSourceState.Waiting => Localization.Get("arch.status.source_waiting", name),
                _ => p.PagesTotal > 0
                    ? Localization.Get("arch.status.source_pages", name, p.PagesDone, p.PagesTotal, p.Maps)
                    : Localization.Get("arch.status.source_loading", name, p.Maps)
            };
            if (p.RequestSentAt.HasValue)
                line += "  " + Localization.Get("arch.status.waiting_reply", (int)(now - p.RequestSentAt.Value).TotalSeconds);
            lines.Add(line);
        }

        if (error != null)
        {
            string retry = _retryAt.HasValue
                ? "  " + Localization.Get("arch.status.retry_in", Math.Max(0, (int)(_retryAt.Value - now).TotalSeconds))
                : "";
            lines.Add(Localization.Get("list.no_connection_retry", error) + retry);
        }

        return string.Join("\n", lines);
    }

    private void UpdateArchitectProgressFill()
    {
        if (_catalogProgressFill == null) return;
        var snapshot = ArchitectLoadStatus.Snapshot();
        if (snapshot.Count == 0) return;

        float sum = 0f;
        foreach (var (_, p) in snapshot)
        {
            if (p.State == ArchitectSourceState.Done) sum += 1f;
            else if (p.PagesTotal > 0) sum += Mathf.Clamp01((float)p.PagesDone / p.PagesTotal);
        }
        ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(sum / snapshot.Count, 1);
    }

    // Высота строки состояния по тексту: длинные ошибки не должны наезжать на кнопку зеркала
    private void FitArchitectStatusText()
    {
        if (_catalogProgressText == null || _progressRect == null) return;

        float width = Mathf.Max(_progressRect.rect.width - 8f, 1f);
        var settings = _catalogProgressText.GetGenerationSettings(new Vector2(width, 0f));
        settings.generateOutOfBounds = true;
        float textHeight = Mathf.Max(ArchitectStatusTextHeight,
            Mathf.Ceil(new TextGenerator().GetPreferredHeight(_catalogProgressText.text, settings) / _catalogProgressText.pixelsPerUnit) + 4f);

        float bottom = ArchitectProgressBarHeight + 2f + textHeight;
        var textRect = (RectTransform)_catalogProgressText.transform;
        textRect.offsetMin = new Vector2(4, -bottom);
        textRect.offsetMax = new Vector2(-4, -(ArchitectProgressBarHeight + 2f));
        bool savedVisible = _savedCatalogGo != null && _savedCatalogGo.activeSelf;
        if (_vpnGuideGo != null)
            ((RectTransform)_vpnGuideGo.transform).anchoredPosition = new Vector2(0, -(bottom + 4f));
        if (_savedCatalogGo != null)
            ((RectTransform)_savedCatalogGo.transform).anchoredPosition = new Vector2(0, -(bottom + 34f));
        if (_mirrorGo != null)
            ((RectTransform)_mirrorGo.transform).anchoredPosition = new Vector2(0, -(bottom + (savedVisible ? 64f : 34f)));

        float extra = textHeight + (_mirrorOffer ? MirrorRowHeight : 0f);
        if (Mathf.Abs(extra - _progressTextExtra) > 0.5f)
        {
            _progressTextExtra = extra;
            LayoutTopArea();
        }
    }

    private void UpdateArchitectStatusText()
    {
        if (CurrentCatalog != MapCatalogKind.ArchitectServer || !_isOpen) return;
        if (!_isLoading && _loadError == null) return;

        _archStatusTimer += Time.unscaledDeltaTime;
        if (_archStatusTimer < ArchitectStatusIntervalSeconds) return;
        _archStatusTimer = 0f;

        if (_catalogProgressText != null)
            _catalogProgressText.text = BuildArchitectStatusText(_loadError);
        UpdateArchitectProgressFill();
        FitArchitectStatusText();
    }

    private void SetCatalogProgressError(string message)
    {
        if (_catalogProgressFill != null)
            ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(0, 1);
        if (_catalogProgressText != null)
            _catalogProgressText.text = Localization.Get("list.no_connection_retry", message);
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1
            ? $"{mb:F1} {Localization.Get("format.mb")}"
            : $"{bytes / 1024.0:F0} {Localization.Get("format.kb")}";
    }

    private void Update()
    {
        if (!_networkSubscribed && NetworkMonitor.Instance != null)
        {
            NetworkMonitor.Instance.OnlineStateChanged += OnNetworkOnlineStateChanged;
            _networkSubscribed = true;
        }
        if (!_progressSubscribed && GlobalListAtlasMod.Instance?.DownloadManager != null)
        {
            GlobalListAtlasMod.Instance.DownloadManager.CatalogLoadProgressChanged += OnCatalogLoadProgressChanged;
            _progressSubscribed = true;
        }
        if (_isOpen) KeepInputOnPanel();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (PopupStack.CloseTop()) return;
            if (_isOpen) { Close(); return; }
        }

        UpdateStatusBar();
        UpdateArchitectStatusText();
        UpdateActiveMapsRow();
        if (!_isOpen) return;
        var inputActions = GameManager.instance?.inputHandler?.inputActions;
        if (inputActions == null) return;
        if (inputActions.down.WasPressed)
        {
            MoveSelection(1);
            StartHold(1);
        }
        else if (inputActions.up.WasPressed)
        {
            MoveSelection(-1);
            StartHold(-1);
        }
        if (_heldDirection == 1 && !inputActions.down.IsPressed)
            StopHold();
        else if (_heldDirection == -1 && !inputActions.up.IsPressed)
            StopHold();
        UpdateHoldRepeat();
    }

    private void UpdateActiveMapsRow()
    {
        if (_activeMapsText == null || !_isOpen) return;

        _activeMapsTimer += Time.unscaledDeltaTime;
        if (_activeMapsTimer < 1f) return;
        _activeMapsTimer = 0f;

        var blocks = ActiveMapResolver.GetActiveMapBlocks();
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        foreach (var (integration, mapName, catalog) in Integrations.PresetManager.GetExclusiveOwners())
        {
            var cached = manager?.FindCachedMap(catalog, mapName);
            if (cached != null) ActiveMapResolver.RememberColor(catalog, mapName, cached.CellColor);
            var color = cached?.CellColor ?? ActiveMapResolver.GetKnownColor(catalog, mapName) ?? new Color32(0xBB, 0xBB, 0xBB, 0xFF);
            blocks.Add($"<color=#C9B7F0>{integration}</color> — {Localization.Get("list.preset_of")} " +
                       $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{mapName}</color>");
        }

        string raw = blocks.Count == 0
            ? Localization.Get("list.nothing_launched")
            : $"{Localization.Get("list.launched_now")}:\n" + string.Join("\n", blocks);

        if (raw == _activeMapsLastRaw) return;
        _activeMapsLastRaw = raw;

        _activeMapsText.color = blocks.Count == 0 ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
        _activeMapsText.text = blocks.Count == 0
            ? raw
            : LayoutBlocks($"{Localization.Get("list.launched_now")}:", blocks);

        float textWidth = Mathf.Max(_activeRect.rect.width - 8f, 1f);
        var settings = _activeMapsText.GetGenerationSettings(new Vector2(textWidth, 0f));
        float height = new TextGenerator().GetPreferredHeight(_activeMapsText.text, settings) / _activeMapsText.pixelsPerUnit;
        float newHeight = Mathf.Max(ActiveMapsRowHeight, Mathf.Ceil(height) + 2f);

        if (!Mathf.Approximately(newHeight, _activeRowHeight))
        {
            _activeRowHeight = newHeight;
            LayoutTopArea();
        }
    }

    private string LayoutBlocks(string prefix, List<string> blocks)
    {
        const string Separator = "   ·   ";

        Canvas.ForceUpdateCanvases();
        float width = Mathf.Max(_activeRect.rect.width - 8f, 1f);
        var settings = _activeMapsText.GetGenerationSettings(Vector2.zero);
        settings.generateOutOfBounds = true;
        var generator = new TextGenerator();
        float Measure(string text) => generator.GetPreferredWidth(text, settings) / _activeMapsText.pixelsPerUnit;

        var lines = new List<string>();
        string current = prefix;
        bool currentHasBlock = false;

        string Join(string line, string block) =>
            string.IsNullOrEmpty(line) ? block : line + (currentHasBlock ? Separator : " ") + block;

        foreach (var block in blocks)
        {
            if (Measure(Join(current, block)) <= width)
            {
                current = Join(current, block);
                currentHasBlock = true;
                continue;
            }

            if (!string.IsNullOrEmpty(current)) lines.Add(current);

            if (Measure(block) <= width)
            {
                current = block;
                currentHasBlock = true;
            }
            else
            {
                lines.Add(block);
                current = "";
                currentHasBlock = false;
            }
        }

        if (!string.IsNullOrEmpty(current)) lines.Add(current);
        return string.Join("\n", lines);
    }

    private void UpdateStatusBar()
    {
        if (_statusBarText == null) return;
        _statusBarTimer += Time.unscaledDeltaTime;
        if (_statusBarTimer < StatusBarUpdateIntervalSeconds) return;
        _statusBarTimer = 0f;
        long activeBps = BandwidthTracker.GetBytesPerSecond();
        bool isDownloading = activeBps > 0;
        var monitor = NetworkMonitor.Instance;
        bool online = (monitor != null && monitor.IsOnline) || isDownloading;
        float pingMs = monitor != null ? monitor.LastPingMs : -1f;
        string pingText = online && pingMs >= 0
            ? $"{pingMs:F0} {Localization.Get("format.ms")}"
            : Localization.Get("status.dash");
        string speedText = isDownloading
            ? BandwidthTracker.FormatSpeed(activeBps)
            : Localization.Get("status.free");
        bool isOffline = GlobalListAtlasMod.Instance?.Settings.IsOfflineMode == true;
        string onlineLabel = isOffline
            ? Localization.Get("status.offline")
            : (online ? Localization.Get("status.online") : Localization.Get("status.no_network"));
        _statusBarText.text = $"{onlineLabel} · {Localization.Get("status.ping")}: {pingText} · {speedText}";
        _statusBarText.color = isOffline
            ? new Color(1f, 0.8f, 0.2f)
            : (online ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.6f, 0.6f));
    }

    private void StartHold(int direction)
    {
        _heldDirection = direction;
        _heldDuration = 0f;
        _timeSinceLastRepeat = 0f;
    }

    private void StopHold()
    {
        _heldDirection = 0;
        _heldDuration = 0f;
        _timeSinceLastRepeat = 0f;
    }

    private void UpdateHoldRepeat()
    {
        if (_heldDirection == 0) return;
        _heldDuration += Time.unscaledDeltaTime;
        if (_heldDuration < HoldRepeatDelaySeconds) return;
        _timeSinceLastRepeat += Time.unscaledDeltaTime;
        float interval = 1f / HoldRepeatRatePerSecond;
        while (_timeSinceLastRepeat >= interval)
        {
            _timeSinceLastRepeat -= interval;
            MoveSelection(_heldDirection);
        }
    }

    private void BuildCanvasIfNeeded()
    {
        if (_canvasGo != null) return;
        UIFactory.CreateRootCanvas("MapListPanelCanvas", out _canvasGo);
        _canvasRoot = (RectTransform)_canvasGo.transform;
        var panel = UIFactory.CreatePanel(_canvasRoot, "MapListPanel", UIFactory.PanelBg);
        panel.anchorMin = new Vector2(0, 0);
        panel.anchorMax = new Vector2(PanelWidthFraction, 1);
        panel.offsetMin = new Vector2(ScreenPadding + PanelHorizontalShift, ScreenPadding);
        panel.offsetMax = new Vector2(-ScreenPadding + PanelHorizontalShift, -ScreenPadding);

        var statusBarRow = new GameObject("StatusBar", typeof(RectTransform));
        statusBarRow.transform.SetParent(panel, false);
        var statusBarRect = (RectTransform)statusBarRow.transform;
        statusBarRect.anchorMin = new Vector2(0, 1);
        statusBarRect.anchorMax = new Vector2(1, 1);
        statusBarRect.pivot = new Vector2(0.5f, 1);
        statusBarRect.sizeDelta = new Vector2(0, StatusBarHeight);
        statusBarRect.anchoredPosition = Vector2.zero;

        _statusBarText = UIFactory.CreateText(statusBarRect, "Text",
            $"{Localization.Get("status.online")} · {Localization.Get("status.ping")}: {Localization.Get("status.dash")} · 0 КБ/с",
            14, TextAnchor.MiddleLeft);
        var statusTextRect = (RectTransform)_statusBarText.transform;
        statusTextRect.anchorMin = Vector2.zero;
        statusTextRect.anchorMax = Vector2.one;
        statusTextRect.offsetMin = new Vector2(4, 0);
        statusTextRect.offsetMax = new Vector2(-205, 0);

        var autoSaveBtnGo = new GameObject("AutoSaveBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        autoSaveBtnGo.transform.SetParent(statusBarRect, false);
        var autoSaveImg = autoSaveBtnGo.GetComponent<Image>();
        autoSaveImg.color = GlobalListAtlasMod.Instance?.Settings.IsAutoSaveCatalog == true
            ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
        var autoSaveBtn = autoSaveBtnGo.GetComponent<Button>();
        autoSaveBtn.transition = Selectable.Transition.None;
        var autoSaveRect = (RectTransform)autoSaveBtnGo.transform;
        autoSaveRect.anchorMin = new Vector2(1, 0.5f);
        autoSaveRect.anchorMax = new Vector2(1, 0.5f);
        autoSaveRect.pivot = new Vector2(1, 0.5f);
        autoSaveRect.sizeDelta = new Vector2(110, 18);
        autoSaveRect.anchoredPosition = new Vector2(-4, 0);
        var autoSaveText = UIFactory.CreateText(autoSaveRect, "Text",
            Localization.Get("button.auto_save"), 12, TextAnchor.MiddleCenter);
        autoSaveText.horizontalOverflow = HorizontalWrapMode.Wrap;
        autoSaveText.verticalOverflow = VerticalWrapMode.Overflow;
        var autoSaveTextRect = (RectTransform)autoSaveText.transform;
        autoSaveTextRect.anchorMin = Vector2.zero; autoSaveTextRect.anchorMax = Vector2.one;
        autoSaveTextRect.offsetMin = Vector2.zero; autoSaveTextRect.offsetMax = Vector2.zero;
        autoSaveBtn.onClick.AddListener(() => {
            if (GlobalListAtlasMod.Instance?.Settings != null)
            {
                bool newState = !GlobalListAtlasMod.Instance.Settings.IsAutoSaveCatalog;
                GlobalListAtlasMod.Instance.Settings.IsAutoSaveCatalog = newState;
                autoSaveImg.color = newState ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);

                GlobalListAtlasMod.Instance.NotifyAutoSaveToggled(newState);

                _ = LoadAndPopulateAsync();
            }
        });

        var offlineBtnGo = new GameObject("OfflineBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        offlineBtnGo.transform.SetParent(statusBarRect, false);
        var offlineImg = offlineBtnGo.GetComponent<Image>();
        bool isCurrentlyOffline = GlobalListAtlasMod.Instance?.Settings.IsOfflineMode == true;
        offlineImg.color = isCurrentlyOffline ? new Color(0.6f, 0.2f, 0.2f) : new Color(0.2f, 0.6f, 0.2f);
        var offlineBtn = offlineBtnGo.GetComponent<Button>();
        offlineBtn.transition = Selectable.Transition.None;
        var offlineRect = (RectTransform)offlineBtnGo.transform;
        offlineRect.anchorMin = new Vector2(1, 0.5f);
        offlineRect.anchorMax = new Vector2(1, 0.5f);
        offlineRect.pivot = new Vector2(1, 0.5f);
        offlineRect.sizeDelta = new Vector2(75, 18);
        offlineRect.anchoredPosition = new Vector2(-123, 0);
        string initialText = isCurrentlyOffline
            ? Localization.Get("status.offline")
            : Localization.Get("status.online");
        var offlineText = UIFactory.CreateText(offlineRect, "Text", initialText, 12, TextAnchor.MiddleCenter);
        offlineText.horizontalOverflow = HorizontalWrapMode.Wrap;
        offlineText.verticalOverflow = VerticalWrapMode.Overflow;
        var offlineTextRect = (RectTransform)offlineText.transform;
        offlineTextRect.anchorMin = Vector2.zero; offlineTextRect.anchorMax = Vector2.one;
        offlineTextRect.offsetMin = Vector2.zero; offlineTextRect.offsetMax = Vector2.zero;
        offlineBtn.onClick.AddListener(() => {
            if (GlobalListAtlasMod.Instance?.Settings != null)
            {
                GlobalListAtlasMod.Instance.Settings.IsOfflineMode =
                    !GlobalListAtlasMod.Instance.Settings.IsOfflineMode;
                bool isOffline = GlobalListAtlasMod.Instance.Settings.IsOfflineMode;
                offlineImg.color = isOffline ? new Color(0.6f, 0.2f, 0.2f) : new Color(0.2f, 0.6f, 0.2f);
                offlineText.text = isOffline
                    ? Localization.Get("status.offline")
                    : Localization.Get("status.online");
                if (_isOpen && !_isLoading) _ = LoadAndPopulateAsync();
            }
        });

        var activeRow = new GameObject("ActiveMapsRow", typeof(RectTransform));
        activeRow.transform.SetParent(panel, false);
        var activeRect = (RectTransform)activeRow.transform;
        activeRect.anchorMin = new Vector2(0, 1);
        activeRect.anchorMax = new Vector2(1, 1);
        activeRect.pivot = new Vector2(0.5f, 1);
        activeRect.sizeDelta = new Vector2(0, ActiveMapsRowHeight);
        activeRect.anchoredPosition = new Vector2(0, -(StatusBarHeight + RowGap));

        _activeRect = activeRect;
        _activeMapsLastRaw = null;
        _activeRowHeight = ActiveMapsRowHeight;
        _activeMapsTimer = 1f;
        _activeMapsText = UIFactory.CreateText(activeRect, "Text", "", 13, TextAnchor.UpperLeft);
        _activeMapsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _activeMapsText.verticalOverflow = VerticalWrapMode.Overflow;
        var activeTextRect = (RectTransform)_activeMapsText.transform;
        activeTextRect.anchorMin = Vector2.zero;
        activeTextRect.anchorMax = Vector2.one;
        activeTextRect.offsetMin = new Vector2(4, 0);
        activeTextRect.offsetMax = new Vector2(-4, 0);

        // Поиск по названию и автору
        var (searchGo, searchInput) = UIFactory.CreateInputField(panel, "SearchField", Localization.Get("list.search_placeholder"), 15);
        var searchRect = (RectTransform)searchGo.transform;
        _searchRect = searchRect;
        searchRect.anchorMin = new Vector2(0, 1);
        searchRect.anchorMax = new Vector2(1, 1);
        searchRect.pivot = new Vector2(0.5f, 1);
        searchRect.sizeDelta = new Vector2(0, SearchRowHeight);
        searchRect.anchoredPosition = new Vector2(0, -(StatusBarHeight + RowGap + ActiveMapsRowHeight + RowGap));
        searchInput.onValueChanged.AddListener(value =>
        {
            _searchQuery = value ?? "";
            RefreshVisibility();
        });

        var filterRow = new GameObject("FilterRow", typeof(RectTransform));
        filterRow.transform.SetParent(panel, false);
        var filterRowRect = (RectTransform)filterRow.transform;
        _filterRowRect = filterRowRect;
        filterRowRect.anchorMin = new Vector2(0, 1);
        filterRowRect.anchorMax = new Vector2(1, 1);
        filterRowRect.pivot = new Vector2(0.5f, 1);
        filterRowRect.sizeDelta = new Vector2(0, FilterRowHeight);
        filterRowRect.anchoredPosition = new Vector2(0, -(StatusBarHeight + RowGap + ActiveMapsRowHeight + RowGap + SearchRowHeight + RowGap));
        const float filterButtonGap = 4f;
        float[] slotWeights = { 0.55f, 1f, 1f, 1f, 1f };
        CreateFilterButton(filterRowRect, "StarsFilterButton",
            Localization.Get("list.filter_stars"), 0, slotWeights, filterButtonGap, OpenStarsFilterPopup);
        CreateFilterButton(filterRowRect, "EditorFilterButton",
            Localization.Get("list.filter_editor"), 1, slotWeights, filterButtonGap, OpenEditorFilterPopup);
        CreateFilterButton(filterRowRect, "TagsFilterButton",
            Localization.Get("list.filter_tags"), 2, slotWeights, filterButtonGap, OpenTagsFilterPopup);
        CreateFilterButton(filterRowRect, "VerifiedFilterButton",
            Localization.Get("list.filter_verified"), 3, slotWeights, filterButtonGap, OpenVerificationFilterPopup);
        CreateFilterButton(filterRowRect, "StatusFilterButton",
            Localization.Get("list.filter_status"), 4, slotWeights, filterButtonGap, OpenStatusFilterPopup);
        UpdateFavoritesButtonColor();
        _globalFilterRow = filterRow;

        var eventFilterRow = new GameObject("EventFilterRow", typeof(RectTransform));
        eventFilterRow.transform.SetParent(panel, false);
        var eventFilterRect = (RectTransform)eventFilterRow.transform;
        _eventFilterRect = eventFilterRect;
        eventFilterRect.anchorMin = filterRowRect.anchorMin;
        eventFilterRect.anchorMax = filterRowRect.anchorMax;
        eventFilterRect.pivot = filterRowRect.pivot;
        eventFilterRect.sizeDelta = filterRowRect.sizeDelta;
        eventFilterRect.anchoredPosition = filterRowRect.anchoredPosition;
        float[] eventSlotWeights = { 1f, 1f, 1f };
        CreateFilterButton(eventFilterRect, "EventTypeFilterButton",
            Localization.Get("list.filter_type"), 0, eventSlotWeights, filterButtonGap, OpenEventTypeFilterPopup);
        CreateFilterButton(eventFilterRect, "EventEditorFilterButton",
            Localization.Get("list.filter_editor"), 1, eventSlotWeights, filterButtonGap, OpenEditorFilterPopup);
        CreateFilterButton(eventFilterRect, "EventStatusFilterButton",
            Localization.Get("list.filter_status"), 2, eventSlotWeights, filterButtonGap, OpenStatusFilterPopup);
        _eventFilterRow = eventFilterRow;

        var serverFilterRow = new GameObject("ServerFilterRow", typeof(RectTransform));
        serverFilterRow.transform.SetParent(panel, false);
        var serverFilterRect = (RectTransform)serverFilterRow.transform;
        _serverFilterRect = serverFilterRect;
        serverFilterRect.anchorMin = filterRowRect.anchorMin;
        serverFilterRect.anchorMax = filterRowRect.anchorMax;
        serverFilterRect.pivot = filterRowRect.pivot;
        serverFilterRect.sizeDelta = filterRowRect.sizeDelta;
        serverFilterRect.anchoredPosition = filterRowRect.anchoredPosition;
        float[] serverSlotWeights = { 1f, 1f, 1f, 1f, 1f };
        CreateFilterButton(serverFilterRect, "ServerSourceFilterButton",
            Localization.Get("list.filter_editor"), 0, serverSlotWeights, filterButtonGap, OpenSourceFilterPopup);
        CreateFilterButton(serverFilterRect, "ServerDifficultyFilterButton",
            Localization.Get("list.filter_difficulty"), 1, serverSlotWeights, filterButtonGap, OpenDifficultyFilterPopup);
        CreateFilterButton(serverFilterRect, "ServerDurationFilterButton",
            Localization.Get("list.filter_duration"), 2, serverSlotWeights, filterButtonGap, OpenDurationFilterPopup);
        CreateFilterButton(serverFilterRect, "ServerTagsFilterButton",
            Localization.Get("list.filter_tags"), 3, serverSlotWeights, filterButtonGap, OpenServerTagsFilterPopup);
        CreateFilterButton(serverFilterRect, "ServerStatusFilterButton",
            Localization.Get("list.filter_status"), 4, serverSlotWeights, filterButtonGap, OpenServerStatusFilterPopup);
        _serverFilterRow = serverFilterRow;

        var (favGo, favButton, favImage, favText) = UIFactory.CreateButton(
            panel, "FavoritesToggleButton", FavoritesGlyph, 18);
        var favRect = (RectTransform)favGo.transform;
        _favRect = favRect;
        favRect.anchorMin = new Vector2(0, 1);
        favRect.anchorMax = new Vector2(0, 1);
        favRect.pivot = new Vector2(0, 1);
        favRect.sizeDelta = new Vector2(FavoritesButtonSize, FavoritesButtonSize);
        favRect.anchoredPosition = new Vector2(4f,
            -(StatusBarHeight + RowGap + ActiveMapsRowHeight + RowGap + SearchRowHeight + RowGap + FilterRowHeight + RowGap));
        favText.alignment = TextAnchor.MiddleCenter;
        var favTextRect = (RectTransform)favText.transform;
        favTextRect.offsetMin = new Vector2(0, favTextRect.offsetMin.y);
        favTextRect.offsetMax = new Vector2(0, favTextRect.offsetMax.y);
        UIFactory.AddOutline(favGo);
        _favoritesButton = (favButton, favImage);
        favButton.onClick.AddListener(() => ToggleFavoritesFilter(favRect));
        UpdateFavoritesButtonColor();

        _markFilterButtons.Clear();
        _markFilterRects.Clear();
        _catalogTabRects.Clear();
        _catalogTabs.Clear();
        _tabRects.Clear();
        foreach (var (name, glyph, getMode, cycle, onColor, glyphColor) in new (string, string, Func<TriFilterMode>, Action, Color, Color)[]
                 {
                     ("SeenFilterButton", "●", () => _seenMode, () => _seenMode = NextMode(_seenMode), new Color(0.22f, 0.40f, 0.52f, 1f), new Color(0.62f, 0.85f, 1f, 1f)),
                     ("LikedFilterButton", "▲", () => _likedOnly ? TriFilterMode.Include : TriFilterMode.Ignore, () => _likedOnly = !_likedOnly, new Color(0.22f, 0.45f, 0.27f, 1f), new Color(0.65f, 1f, 0.68f, 1f)),
                     ("DislikedFilterButton", "▼", () => _dislikedOnly ? TriFilterMode.Include : TriFilterMode.Ignore, () => _dislikedOnly = !_dislikedOnly, new Color(0.50f, 0.22f, 0.24f, 1f), new Color(1f, 0.66f, 0.66f, 1f))
                 })
        {
            var (markGo, markButton, markImage, markText) = UIFactory.CreateButton(panel, name, glyph, 16);
            markText.alignment = TextAnchor.MiddleCenter;
            markText.color = glyphColor;
            var markTextRect = (RectTransform)markText.transform;
            markTextRect.offsetMin = new Vector2(0, markTextRect.offsetMin.y);
            markTextRect.offsetMax = new Vector2(0, markTextRect.offsetMax.y);
            UIFactory.AddOutline(markGo);
            var markRect = (RectTransform)markGo.transform;
            markRect.anchorMin = new Vector2(0, 1);
            markRect.anchorMax = new Vector2(0, 1);
            markRect.pivot = new Vector2(0, 1);
            markRect.sizeDelta = new Vector2(FavoritesButtonSize, FavoritesButtonSize);

            var capturedCycle = cycle;
            markButton.onClick.AddListener(() =>
            {
                capturedCycle();
                UpdateMarkFilterColors();
                RefreshVisibility();
            });
            _markFilterButtons.Add((markImage, markText, getMode, onColor, glyph));
            _markFilterRects.Add(markRect);
            _tabRects.Add(markRect);
        }
        UpdateMarkFilterColors();

        foreach (var (kind, key) in new[] { (MapCatalogKind.GlobalList, "catalog.globallist"),
                                            (MapCatalogKind.EventCommunity, "catalog.events"),
                                            (MapCatalogKind.ArchitectServer, "catalog.architect") })
        {
            var (tabGo, tabButton, tabImage, tabText) = UIFactory.CreateButton(panel, $"CatalogTab_{kind}", Localization.Get(key), 15);
            tabText.alignment = TextAnchor.MiddleCenter;
            tabText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.AddOutline(tabGo);
            var tabRect = (RectTransform)tabGo.transform;
            tabRect.anchorMin = new Vector2(0, 1);
            tabRect.anchorMax = new Vector2(0, 1);
            tabRect.pivot = new Vector2(0, 1);
            tabRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(tabText, min: 110f), FavoritesButtonSize);

            var capturedKind = kind;
            tabButton.onClick.AddListener(() => OnCatalogTabClicked(capturedKind));
            _catalogTabs.Add((kind, tabImage, tabText));
            _catalogTabRects[kind] = tabRect;
            _tabRects.Add(tabRect);
        }

        var (sortGo, sortButton, sortImage, sortText) = UIFactory.CreateButton(panel, "ServerSortButton", SortButtonLabel(), 15);
        sortText.alignment = TextAnchor.MiddleCenter;
        sortText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UIFactory.AddOutline(sortGo);
        var sortRect = (RectTransform)sortGo.transform;
        sortRect.anchorMin = new Vector2(1, 1);
        sortRect.anchorMax = new Vector2(1, 1);
        sortRect.pivot = new Vector2(1, 1);
        sortRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(sortText, min: 110f), FavoritesButtonSize);
        sortRect.anchoredPosition = new Vector2(-(4f + FavoritesButtonSize + TopRowGap), favRect.anchoredPosition.y);
        sortButton.onClick.AddListener(ToggleServerSort);
        _sortButtonText = sortText;
        _sortButtonRect = sortRect;
        _tabRects.Add(sortRect);

        var (devToolsGo, devToolsButton, devToolsImage, _) = UIFactory.CreateButton(
            panel, "DevToolsButton", "", 16);
        UIFactory.AddOutline(devToolsGo);
        var devToolsIconGo = new GameObject("KeyIcon", typeof(RectTransform), typeof(Image));
        devToolsIconGo.transform.SetParent(devToolsGo.transform, false);
        var devToolsIcon = devToolsIconGo.GetComponent<Image>();
        devToolsIcon.sprite = UIFactory.GetIcon(UIFactory.IconKind.Wrench);
        devToolsIcon.raycastTarget = false;
        var devToolsIconRect = (RectTransform)devToolsIconGo.transform;
        devToolsIconRect.anchorMin = new Vector2(0.5f, 0.5f);
        devToolsIconRect.anchorMax = new Vector2(0.5f, 0.5f);
        devToolsIconRect.sizeDelta = new Vector2(FavoritesButtonSize - 8f, FavoritesButtonSize - 8f);
        devToolsIconRect.anchoredPosition = Vector2.zero;
        var devToolsRect = (RectTransform)devToolsGo.transform;
        devToolsRect.anchorMin = new Vector2(1, 1);
        devToolsRect.anchorMax = new Vector2(1, 1);
        devToolsRect.pivot = new Vector2(1, 1);
        devToolsRect.sizeDelta = new Vector2(FavoritesButtonSize, FavoritesButtonSize);
        devToolsRect.anchoredPosition = new Vector2(-4f, favRect.anchoredPosition.y);
        _devToolsButton = (devToolsButton, devToolsImage);
        _devToolsIcon = devToolsIcon;
        devToolsButton.onClick.AddListener(OnDevToolsClicked);
        _tabRects.Add(devToolsRect);
        UpdateDevToolsButtonColor();

        var (reloadGo, reloadButton, reloadImage, _) = UIFactory.CreateButton(panel, "ParserReloadButton", "", 13);
        reloadImage.color = UIFactory.ButtonBg;
        var reloadRect = (RectTransform)reloadGo.transform;
        reloadRect.anchorMin = new Vector2(1, 1);
        reloadRect.anchorMax = new Vector2(1, 1);
        reloadRect.pivot = new Vector2(1, 1);
        reloadRect.sizeDelta = new Vector2(FavoritesButtonSize, FavoritesButtonSize);
        reloadRect.anchoredPosition = new Vector2(-(4f + FavoritesButtonSize + TopRowGap), favRect.anchoredPosition.y);

        var ringSprite = UIFactory.GetIcon(UIFactory.IconKind.Ring);
        var ringBgGo = new GameObject("RingBg", typeof(RectTransform), typeof(Image));
        ringBgGo.transform.SetParent(reloadGo.transform, false);
        var ringBg = ringBgGo.GetComponent<Image>();
        ringBg.sprite = ringSprite;
        ringBg.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        ringBg.raycastTarget = false;
        var ringBgRect = (RectTransform)ringBgGo.transform;
        ringBgRect.sizeDelta = new Vector2(FavoritesButtonSize - 6f, FavoritesButtonSize - 6f);

        var ringFillGo = new GameObject("RingFill", typeof(RectTransform), typeof(Image));
        ringFillGo.transform.SetParent(reloadGo.transform, false);
        var ringFill = ringFillGo.GetComponent<Image>();
        ringFill.sprite = ringSprite;
        ringFill.color = new Color(0.85f, 0.85f, 0.95f, 1f);
        ringFill.type = Image.Type.Filled;
        ringFill.fillMethod = Image.FillMethod.Radial360;
        ringFill.fillOrigin = (int)Image.Origin360.Top;
        ringFill.fillClockwise = true;
        ringFill.fillAmount = 0f;
        ringFill.raycastTarget = false;
        var ringFillRect = (RectTransform)ringFillGo.transform;
        ringFillRect.sizeDelta = new Vector2(FavoritesButtonSize - 6f, FavoritesButtonSize - 6f);

        var reloadHold = reloadGo.AddComponent<HoldToConfirmButton>();
        reloadHold.Fill = ringFill;
        reloadHold.OnConfirmed = () =>
        {
            var manager = GlobalListAtlasMod.Instance?.DownloadManager;
            if (manager != null && manager.CurrentCatalog == MapCatalogKind.ArchitectServer)
            {
                ArchitectServerConfig.UseMirror = false;
                _mirrorOffer = false;
                manager.InvalidateServerCatalog();
                _entries.Clear();
                RebuildButtons();
                RefreshVisibility();
                _ = LoadAndPopulateAsync();
            }
            reloadHold.ResetState();
        };
        _reloadRect = reloadRect;
        _tabRects.Add(reloadRect);

        sortRect.anchoredPosition = new Vector2(
            -(4f + FavoritesButtonSize + TopRowGap + reloadRect.sizeDelta.x + TopRowGap),
            favRect.anchoredPosition.y);

        UpdateCatalogUi();

        var (progressGo, progressFill, progressText) = UIFactory.CreateProgressBar(panel, "CatalogProgressBar");
        var progressRect = (RectTransform)progressGo.transform;
        _progressRect = progressRect;
        progressRect.anchorMin = new Vector2(0, 1);
        progressRect.anchorMax = new Vector2(1, 1);
        progressRect.pivot = new Vector2(0.5f, 1);
        progressRect.sizeDelta = new Vector2(0, CatalogProgressBarHeight);
        progressRect.anchoredPosition = new Vector2(0, -(StatusBarHeight + RowGap + ActiveMapsRowHeight + RowGap + SearchRowHeight + RowGap + FilterRowHeight + RowGap + FavoritesButtonSize + RowGap));
        progressText.fontSize = 13;
        _catalogProgressGo = progressGo;
        _catalogProgressFill = progressFill;
        _catalogProgressText = progressText;
        _catalogProgressGo.SetActive(false);

        var (mirrorGo, mirrorButton, mirrorImage, mirrorLabel) = UIFactory.CreateButton(
            progressGo.transform, "MirrorSwitchButton", Localization.Get("arch.mirror.offer"), 12);
        mirrorLabel.alignment = TextAnchor.MiddleCenter;
        mirrorLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        mirrorLabel.color = new Color(1f, 0.72f, 0.6f, 1f);
        UIFactory.AddOutline(mirrorGo);
        var mirrorRect = (RectTransform)mirrorGo.transform;
        mirrorRect.anchorMin = new Vector2(0, 1);
        mirrorRect.anchorMax = new Vector2(0, 1);
        mirrorRect.pivot = new Vector2(0, 1);
        mirrorRect.sizeDelta = new Vector2(280f, 26f);
        mirrorRect.anchoredPosition = new Vector2(0, -(ArchitectProgressBarHeight + 2f + ArchitectStatusTextHeight + 4f));
        mirrorButton.onClick.AddListener(OnMirrorSwitchClicked);
        mirrorGo.SetActive(false);
        _mirrorGo = mirrorGo;

        var (vpnGuideGo, vpnGuideButton, vpnGuideImage, vpnGuideLabel) = UIFactory.CreateButton(
            progressGo.transform, "VpnGuideButton", Localization.Get("arch.vpn.guide"), 12);
        vpnGuideLabel.alignment = TextAnchor.MiddleCenter;
        vpnGuideLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        UIFactory.AddOutline(vpnGuideGo);
        var vpnGuideRect = (RectTransform)vpnGuideGo.transform;
        vpnGuideRect.anchorMin = new Vector2(0, 1);
        vpnGuideRect.anchorMax = new Vector2(0, 1);
        vpnGuideRect.pivot = new Vector2(0, 1);
        vpnGuideRect.sizeDelta = new Vector2(280f, 26f);
        vpnGuideRect.anchoredPosition = new Vector2(0, -(ArchitectProgressBarHeight + 2f + ArchitectStatusTextHeight + 4f));
        vpnGuideButton.onClick.AddListener(() => HelpPopup.ShowVpnGuide());
        vpnGuideGo.SetActive(false);
        _vpnGuideGo = vpnGuideGo;

        var (savedGo, savedButton, savedImage, savedLabel) = UIFactory.CreateButton(
            progressGo.transform, "SavedCatalogButton", Localization.Get("arch.saved.load"), 12);
        savedLabel.alignment = TextAnchor.MiddleCenter;
        savedLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        UIFactory.AddOutline(savedGo);
        var savedRect = (RectTransform)savedGo.transform;
        savedRect.anchorMin = new Vector2(0, 1);
        savedRect.anchorMax = new Vector2(0, 1);
        savedRect.pivot = new Vector2(0, 1);
        savedRect.sizeDelta = new Vector2(280f, 26f);
        savedButton.onClick.AddListener(OnSavedCatalogClicked);
        savedGo.SetActive(false);
        _savedCatalogGo = savedGo;

        var (content, scrollRect) = UIFactory.CreateVerticalScrollList(panel, "MapListScroll");
        _scrollRect = scrollRect;
        var scrollRootRect = (RectTransform)content.parent.parent;
        scrollRootRect.anchorMin = new Vector2(0, 0);
        scrollRootRect.anchorMax = new Vector2(1, 1);
        scrollRootRect.offsetMin = new Vector2(0, 0);
        _listScrollRoot = scrollRootRect;
        _listContent = content;
        LayoutTopArea();

        float langButtonSize = 40f;
        float langButtonGap = 8f;
        float langPanelOffset = 60f;
        var langContainer = new GameObject("LanguageButtons", typeof(RectTransform));
        langContainer.transform.SetParent(panel, false);
        var langContainerRect = langContainer.GetComponent<RectTransform>();
        langContainerRect.anchorMin = new Vector2(0, 1);
        langContainerRect.anchorMax = new Vector2(0, 1);
        langContainerRect.pivot = new Vector2(0, 1);
        langContainerRect.sizeDelta = new Vector2(langButtonSize, langButtonSize * 2 + langButtonGap);
        langContainerRect.anchoredPosition = new Vector2(-langPanelOffset, -10);

        var langRuBtnGo = new GameObject("LangRuButton", typeof(RectTransform), typeof(Image), typeof(Button));
        langRuBtnGo.transform.SetParent(langContainer.transform, false);
        var langRuRect = langRuBtnGo.GetComponent<RectTransform>();
        langRuRect.anchorMin = new Vector2(0.5f, 0.5f);
        langRuRect.anchorMax = new Vector2(0.5f, 0.5f);
        langRuRect.pivot = new Vector2(0.5f, 0.5f);
        langRuRect.sizeDelta = new Vector2(langButtonSize, langButtonSize);
        langRuRect.anchoredPosition = new Vector2(0, langButtonSize / 2 + langButtonGap / 2);
        var langRuBg = langRuBtnGo.GetComponent<Image>();
        langRuBg.color = Color.white;
        _langRuButton = langRuBtnGo.GetComponent<Button>();
        _langRuButton.transition = Selectable.Transition.None;
        _langRuImage = langRuBg;
        Sprite ruSprite = LoadFlagSprite("ru");
        if (ruSprite != null)
        {
            langRuBg.sprite = ruSprite;
            langRuBg.preserveAspect = false;
        }
        _langRuButton.onClick.AddListener(() => {
            GlobalListAtlasMod.Instance?.ToggleLanguage();
            RefreshAllUI();
        });

        var langEnBtnGo = new GameObject("LangEnButton", typeof(RectTransform), typeof(Image), typeof(Button));
        langEnBtnGo.transform.SetParent(langContainer.transform, false);
        var langEnRect = langEnBtnGo.GetComponent<RectTransform>();
        langEnRect.anchorMin = new Vector2(0.5f, 0.5f);
        langEnRect.anchorMax = new Vector2(0.5f, 0.5f);
        langEnRect.pivot = new Vector2(0.5f, 0.5f);
        langEnRect.sizeDelta = new Vector2(langButtonSize, langButtonSize);
        langEnRect.anchoredPosition = new Vector2(0, -langButtonSize / 2 - langButtonGap / 2);
        var langEnBg = langEnBtnGo.GetComponent<Image>();
        langEnBg.color = Color.white;
        _langEnButton = langEnBtnGo.GetComponent<Button>();
        _langEnButton.transition = Selectable.Transition.None;
        _langEnImage = langEnBg;
        Sprite enSprite = LoadFlagSprite("en");
        if (enSprite != null)
        {
            langEnBg.sprite = enSprite;
            langEnBg.preserveAspect = false;
        }
        _langEnButton.onClick.AddListener(() => {
            GlobalListAtlasMod.Instance?.ToggleLanguage();
            RefreshAllUI();
        });
    }

    private void RebuildButtons()
    {
        foreach (var go in _rowGos) Destroy(go);
        _rowGos.Clear();
        _buttonGos.Clear();
        _buttonImages.Clear();
        _displayEntryIndices.Clear();
        _headerGosByLeague.Clear();
        _headerGosByGroup.Clear();

        if (CurrentCatalog == MapCatalogKind.ArchitectServer)
        {
            RebuildServerButtons();
            return;
        }

        bool isEventCatalog = CurrentCatalog == MapCatalogKind.EventCommunity;

        var orderedIndices = isEventCatalog
            ? Enumerable.Range(0, _entries.Count).ToList()
            : Enumerable.Range(0, _entries.Count)
                .OrderBy(i => LeagueConfig.GetOrder(_entries[i].League))
                .ThenBy(i => i)
                .ToList();
        League? currentLeague = null;
        foreach (int originalIndex in orderedIndices)
        {
            var entry = _entries[originalIndex];
            if (!isEventCatalog && (currentLeague == null || entry.League != currentLeague))
            {
                bool firstHeader = currentLeague == null;
                currentLeague = entry.League;
                var headerGos = CreateLeagueHeader(entry.League, firstHeader);
                _rowGos.AddRange(headerGos);
                if (!_headerGosByLeague.TryGetValue(entry.League, out var list))
                {
                    list = new List<GameObject>();
                    _headerGosByLeague[entry.League] = list;
                }
                list.AddRange(headerGos);
            }
            string label = isEventCatalog
                ? $"{entry.Name}   <size=14>{EventCatalogConfig.GetTypeLabel(entry.EventType)}</size>"
                : $"{entry.Name}   {StarsToString(entry.Stars)}";
            var (btnGo, button, image, text) = UIFactory.CreateButton(
                _listContent, $"MapButton_{originalIndex}", label, 20);
            var rect = (RectTransform)btnGo.transform;
            rect.sizeDelta = new Vector2(0, 44);
            image.color = entry.CellColor;
            text.color = GetReadableTextColor(entry.CellColor);
            int capturedButtonIndex = _buttonGos.Count;
            button.onClick.AddListener(() => SelectByButtonIndex(capturedButtonIndex));
            _rowGos.Add(btnGo);
            _buttonGos.Add(btnGo);
            _buttonImages.Add(image);
            _displayEntryIndices.Add(originalIndex);
        }
        _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, 0f);
    }

    private void RebuildServerButtons()
    {
        var ordered = Enumerable.Range(0, _entries.Count);
        ordered = _serverSort == ServerSortMode.UploadDate
            ? ordered.OrderByDescending(i => _entries[i].Uploaded ?? DateTime.MinValue)
                .ThenBy(i => _entries[i].ServerOrder).ThenBy(i => _entries[i].Name)
            : ordered.OrderByDescending(i => _entries[i].Downloads).ThenBy(i => _entries[i].Name);

        string currentGroup = null;
        foreach (int originalIndex in ordered)
        {
            var entry = _entries[originalIndex];
            string group = GetServerGroupKey(entry);
            if (group != currentGroup)
            {
                bool firstHeader = currentGroup == null;
                currentGroup = group;
                var headerGos = CreateGroupHeader(group, GetServerGroupLabel(entry), firstHeader);
                _rowGos.AddRange(headerGos);
                _headerGosByGroup[group] = new List<GameObject>(headerGos);
            }

            var (btnGo, button, image, text) = UIFactory.CreateButton(
                _listContent, $"MapButton_{originalIndex}", entry.Name, 20);
            text.alignment = TextAnchor.MiddleLeft;
            ((RectTransform)text.transform).offsetMax = new Vector2(-170f, -2f);
            ((RectTransform)btnGo.transform).sizeDelta = new Vector2(0, 44);
            image.color = entry.CellColor;
            text.color = GetReadableTextColor(entry.CellColor);

            var statsRect = UIFactory.CreateStatsRow(btnGo.transform, $"↓ {entry.Downloads}",
                entry.HasServerMetadata ? entry.Likes : null, text.color, 15, 30f);
            statsRect.anchorMin = new Vector2(1, 0.5f);
            statsRect.anchorMax = new Vector2(1, 0.5f);
            statsRect.pivot = new Vector2(1, 0.5f);
            statsRect.anchoredPosition = new Vector2(-10f, 0f);

            int capturedButtonIndex = _buttonGos.Count;
            button.onClick.AddListener(() => SelectByButtonIndex(capturedButtonIndex));
            _rowGos.Add(btnGo);
            _buttonGos.Add(btnGo);
            _buttonImages.Add(image);
            _displayEntryIndices.Add(originalIndex);
        }

        _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, 0f);
    }

    private string GetServerGroupKey(MapRow map)
    {
        if (_serverSort == ServerSortMode.Downloads)
            return "downloads_" + DownloadThresholds.Last(t => map.Downloads >= t);

        return map.Uploaded.HasValue ? map.Uploaded.Value.ToString("yyyy-MM") : "undated";
    }

    private string GetServerGroupLabel(MapRow map)
    {
        if (_serverSort == ServerSortMode.Downloads)
        {
            int index = Array.FindLastIndex(DownloadThresholds, t => map.Downloads >= t);
            int from = DownloadThresholds[index];
            return index == DownloadThresholds.Length - 1
                ? $"↓ {from}+"
                : $"↓ {from}–{DownloadThresholds[index + 1] - 1}";
        }

        if (!map.Uploaded.HasValue)
            return Localization.Get("server.group_undated");

        var culture = GlobalListAtlasMod.Instance?.Settings.CurrentLanguage == "ru"
            ? new System.Globalization.CultureInfo("ru-RU")
            : new System.Globalization.CultureInfo("en-US");
        string month = culture.DateTimeFormat.GetMonthName(map.Uploaded.Value.Month);
        return $"{char.ToUpper(month[0], culture)}{month.Substring(1)} {map.Uploaded.Value.Year}";
    }

    private GameObject[] CreateGroupHeader(string key, string label, bool firstHeader)
    {
        var spacer = firstHeader ? null : AddHeaderSpacer($"Spacer_{key}");
        var text = UIFactory.CreateText(_listContent, $"Header_{key}", label, 16, TextAnchor.MiddleLeft);
        ((RectTransform)text.transform).sizeDelta = new Vector2(0, 26);
        return spacer != null ? new[] { spacer, text.gameObject } : new[] { text.gameObject };
    }

    private GameObject AddHeaderSpacer(string name)
    {
        var spacerGo = new GameObject(name, typeof(RectTransform));
        spacerGo.transform.SetParent(_listContent, false);
        ((RectTransform)spacerGo.transform).sizeDelta = new Vector2(0, 14);
        return spacerGo;
    }

    private GameObject[] CreateLeagueHeader(League league, bool firstHeader)
    {
        var spacer = firstHeader ? null : AddHeaderSpacer($"Spacer_{league}");
        var text = UIFactory.CreateText(_listContent, $"Header_{league}",
            LeagueConfig.GetLocalizedLabel(league), 16, TextAnchor.MiddleLeft);
        ((RectTransform)text.transform).sizeDelta = new Vector2(0, 26);
        return spacer != null ? new[] { spacer, text.gameObject } : new[] { text.gameObject };
    }

    private static string StarsToString(int stars)
    {
        stars = Mathf.Clamp(stars, 0, 5);
        return new string('★', stars) + new string('☆', 5 - stars);
    }

    private static Color GetReadableTextColor(Color32 bg)
    {
        float luminance = (0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b) / 255f;
        return luminance > 0.6f ? Color.black : Color.white;
    }

    private static Color DarkenColor(Color32 c, float factor = 0.55f)
    {
        return new Color(c.r / 255f * factor, c.g / 255f * factor, c.b / 255f * factor, 1f);
    }

    private static (Button Button, Image Image) CreateFilterButton(
        RectTransform parent, string name, string label,
        int slotIndex, float[] slotWeights, float gap, Action<RectTransform> onClick)
    {
        var (go, button, image, text) = UIFactory.CreateButton(parent, name, label, 15);
        text.alignment = TextAnchor.MiddleCenter;
        UIFactory.AddOutline(go);
        var rect = (RectTransform)go.transform;

        float total = slotWeights.Sum();
        float start = slotWeights.Take(slotIndex).Sum() / total;
        float end = slotWeights.Take(slotIndex + 1).Sum() / total;

        rect.anchorMin = new Vector2(start, 0);
        rect.anchorMax = new Vector2(end, 1);

        float leftGap = slotIndex == 0 ? 0f : gap / 2f;
        float rightGap = slotIndex == slotWeights.Length - 1 ? 0f : gap / 2f;
        rect.offsetMin = new Vector2(leftGap, 0);
        rect.offsetMax = new Vector2(-rightGap, 0);

        button.onClick.AddListener(() => onClick(rect));
        return (button, image);
    }

    private void OpenStarsFilterPopup(RectTransform anchor)
    {
        var options = new List<FilterOption>();
        for (int s = 0; s <= 5; s++)
        {
            options.Add(new FilterOption
            {
                Label = s == 0 ? Localization.Get("filter.stars.no_stars") : $"{s} ★",
                IsOn = _selectedStars.Contains(s),
                Value = s
            });
        }
        MultiToggleFilterPopup.Show(_canvasRoot, anchor,
            Localization.Get("filter.stars.title"), options, _starsShowAll,
            onShowAllSelected: () =>
            {
                _starsShowAll = true;
                _selectedStars.Clear();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                int stars = (int)option.Value;
                _starsShowAll = false;
                if (option.IsOn) _selectedStars.Add(stars);
                else
                {
                    _selectedStars.Remove(stars);
                    if (_selectedStars.Count == 0) _starsShowAll = true;
                }
                RefreshVisibility();
            });
    }

    private void OpenVerificationFilterPopup(RectTransform anchor)
    {
        var options = new List<FilterOption>
        {
            new() { Label = Localization.Get("filter.verified.verified"),
                    IsOn = _selectedVerification.Contains(true), Value = true },
            new() { Label = Localization.Get("filter.verified.not_verified"),
                    IsOn = _selectedVerification.Contains(false), Value = false },
        };
        MultiToggleFilterPopup.Show(_canvasRoot, anchor,
            Localization.Get("filter.verified.title"), options, _verificationShowAll,
            onShowAllSelected: () =>
            {
                _verificationShowAll = true;
                _selectedVerification.Clear();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                bool verified = (bool)option.Value;
                _verificationShowAll = false;
                if (option.IsOn) _selectedVerification.Add(verified);
                else
                {
                    _selectedVerification.Remove(verified);
                    if (_selectedVerification.Count == 0) _verificationShowAll = true;
                }
                RefreshVisibility();
            });
    }

    private void OpenEditorFilterPopup(RectTransform anchor)
    {
        var options = EditorConfig.Palette
            .Select(p => new FilterOption
            {
                Label = p.Label,
                IsOn = _selectedEditors.Contains(p.Editor),
                Value = p.Editor,
                AccentColor = p.Color
            })
            .ToList();
        MultiToggleFilterPopup.Show(_canvasRoot, anchor,
            Localization.Get("filter.editor.title"), options, _editorsShowAll,
            onShowAllSelected: () =>
            {
                _editorsShowAll = true;
                _selectedEditors.Clear();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                var editor = (MapEditor)option.Value;
                _editorsShowAll = false;
                if (option.IsOn) _selectedEditors.Add(editor);
                else
                {
                    _selectedEditors.Remove(editor);
                    if (_selectedEditors.Count == 0) _editorsShowAll = true;
                }
                RefreshVisibility();
            });
    }

    private void OpenTagsFilterPopup(RectTransform anchor)
    {
        var options = TagConfig.Palette
            .Select(p => new FilterOption
            {
                Label = p.Label,
                IsOn = _selectedTags.Contains(p.Tag),
                Value = p.Tag,
                AccentColor = p.Color
            })
            .ToList();
        MultiToggleFilterPopup.Show(_canvasRoot, anchor,
            Localization.Get("filter.tags.title"), options, _tagsShowAll,
            onShowAllSelected: () =>
            {
                _tagsShowAll = true;
                _selectedTags.Clear();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                var tag = (MapTag)option.Value;
                _tagsShowAll = false;
                if (option.IsOn) _selectedTags.Add(tag);
                else
                {
                    _selectedTags.Remove(tag);
                    if (_selectedTags.Count == 0) _tagsShowAll = true;
                }
                RefreshVisibility();
            });
    }

    private void OpenStatusFilterPopup(RectTransform anchor)
    {
        var options = new List<FilterOption>
        {
            new() { Label = Localization.Get("filter.status.not_downloaded"),
                    IsOn = _selectedStatuses.Contains(MapStatus.NotDownloaded),
                    Value = MapStatus.NotDownloaded,
                    AccentColor = new Color32(180, 60, 60, 255) },
            new() { Label = Localization.Get("filter.status.installed"),
                    IsOn = _selectedStatuses.Contains(MapStatus.Installed),
                    Value = MapStatus.Installed,
                    AccentColor = new Color32(200, 160, 40, 255) },
            new() { Label = Localization.Get("filter.status.running"),
                    IsOn = _selectedStatuses.Contains(MapStatus.Running),
                    Value = MapStatus.Running,
                    AccentColor = new Color32(50, 160, 70, 255) }
        };
        MultiToggleFilterPopup.Show(_canvasRoot, anchor,
            Localization.Get("filter.status.title"), options, _statusesShowAll,
            onShowAllSelected: () =>
            {
                _statusesShowAll = true;
                _selectedStatuses.Clear();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                var status = (MapStatus)option.Value;
                _statusesShowAll = false;
                if (option.IsOn) _selectedStatuses.Add(status);
                else
                {
                    _selectedStatuses.Remove(status);
                    if (_selectedStatuses.Count == 0) _statusesShowAll = true;
                }
                RefreshVisibility();
            });
    }

    private static MapStatus GetMapStatus(MapRow entry)
    {
        if (entry == null) return MapStatus.NotDownloaded;
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return MapStatus.NotDownloaded;
        if (manager.IsMapLaunched(entry)) return MapStatus.Running;
        if (manager.IsMapDownloaded(entry)) return MapStatus.Installed;
        return MapStatus.NotDownloaded;
    }

    private void ToggleFavoritesFilter(RectTransform _)
    {
        _favoritesOnly = !_favoritesOnly;
        UpdateFavoritesButtonColor();
        RefreshVisibility();
    }

    private void UpdateFavoritesButtonColor()
    {
        if (_favoritesButton.Image == null) return;

        _favoritesButton.Image.color = _favoritesOnly
            ? new Color(0.478f, 0.192f, 0.255f, 1f)
            : new Color(0.157f, 0.145f, 0.180f, 1f);
    }

    private bool MatchesSearch(MapRow entry)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery)) return true;

        string query = _searchQuery.Trim();
        return (entry.Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            || (entry.Creator?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static MapCatalogKind CurrentCatalog =>
        GlobalListAtlasMod.Instance?.DownloadManager?.CurrentCatalog ?? MapCatalogKind.GlobalList;

    private void OnCatalogTabClicked(MapCatalogKind kind)
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null || manager.CurrentCatalog == kind) return;
        if (kind == MapCatalogKind.ArchitectServer)
        {
            ArchitectServerConfig.UseMirror = false;
            _mirrorOffer = false;
        }

        PopupStack.CloseAll();
        manager.SetCatalog(kind);
        ResetAllFilters();
        UpdateCatalogUi();

        _entries.Clear();
        RebuildButtons();
        RefreshVisibility();
        _ = LoadAndPopulateAsync();
    }

    private void UpdateCatalogUi()
    {
        bool isEvent = CurrentCatalog == MapCatalogKind.EventCommunity;

        bool isServer = CurrentCatalog == MapCatalogKind.ArchitectServer;
        if (_globalFilterRow != null) _globalFilterRow.SetActive(!isEvent && !isServer);
        if (_eventFilterRow != null) _eventFilterRow.SetActive(isEvent);
        if (_serverFilterRow != null) _serverFilterRow.SetActive(isServer);

        foreach (var (kind, image, text) in _catalogTabs)
        {
            if (image == null || text == null) continue;

            bool active = kind == CurrentCatalog;
            image.color = active ? UIFactory.ToggleOnBg : UIFactory.ButtonBg;
            text.color = UIFactory.GetReadableTextColor(image.color);
        }

        LayoutCatalogRow();
    }

    private static bool IsDevToolsMode => GlobalListAtlasMod.Instance?.Settings.IsDevToolsUnlocked == true;

    private void LayoutCatalogRow()
    {
        if (_favRect == null || _catalogTabRects.Count == 0) return;

        bool devMode = IsDevToolsMode;
        foreach (var rect in _markFilterRects) rect.gameObject.SetActive(devMode);
        if (_sortButtonRect != null) _sortButtonRect.gameObject.SetActive(devMode);
        if (_reloadRect != null) _reloadRect.gameObject.SetActive(devMode);
        _catalogTabRects[MapCatalogKind.GlobalList].gameObject.SetActive(!devMode);
        _catalogTabRects[MapCatalogKind.EventCommunity].gameObject.SetActive(!devMode);
        _catalogTabRects[MapCatalogKind.ArchitectServer].gameObject.SetActive(devMode);

        var row = new List<RectTransform>();
        if (devMode)
        {
            row.AddRange(_markFilterRects);
            row.Add(_catalogTabRects[MapCatalogKind.ArchitectServer]);
        }
        else
        {
            row.Add(_catalogTabRects[MapCatalogKind.GlobalList]);
            row.Add(_catalogTabRects[MapCatalogKind.EventCommunity]);
        }

        float x = _favRect.anchoredPosition.x + FavoritesButtonSize + TopRowGap;
        float y = _favRect.anchoredPosition.y;
        foreach (var rect in row)
        {
            rect.anchoredPosition = new Vector2(x, y);
            x += rect.sizeDelta.x + TopRowGap;
        }
    }

    private void OnDevToolsClicked()
    {
        var settings = GlobalListAtlasMod.Instance?.Settings;
        if (settings == null) return;

        settings.IsDevToolsUnlocked = !settings.IsDevToolsUnlocked;
        UpdateDevToolsButtonColor();

        var target = settings.IsDevToolsUnlocked ? MapCatalogKind.ArchitectServer : MapCatalogKind.GlobalList;
        if (CurrentCatalog == target)
            UpdateCatalogUi();
        else
            OnCatalogTabClicked(target);
    }

    private void UpdateDevToolsButtonColor()
    {
        if (_devToolsButton.Image == null) return;
        bool unlocked = GlobalListAtlasMod.Instance?.Settings.IsDevToolsUnlocked == true;
        _devToolsButton.Image.color = unlocked ? UIFactory.ToggleOnBg : UIFactory.ButtonBg;
        if (_devToolsIcon != null)
            _devToolsIcon.color = UIFactory.GetReadableTextColor(_devToolsButton.Image.color);
    }

    private void ResetAllFilters()
    {
        _starsShowAll = true; _selectedStars.Clear();
        _editorsShowAll = true; _selectedEditors.Clear();
        _verificationShowAll = true; _selectedVerification.Clear();
        _tagsShowAll = true; _selectedTags.Clear();
        _statusesShowAll = true; _selectedStatuses.Clear();
        _eventTypesShowAll = true; _selectedEventTypes.Clear();
        _sourceFilter.Reset();
        _difficultyFilter.Reset();
        _durationFilter.Reset();
        _serverTagFilter.Reset();
        _showSilksong = false; // Silksong по умолчанию скрыт

        _seenMode = TriFilterMode.Ignore; _likedOnly = false; _dislikedOnly = false;
        _serverStatusFilter.Reset();
        UpdateMarkFilterColors();
    }

    private void OpenEventTypeFilterPopup(RectTransform anchor)
    {
        var options = new List<FilterOption>
        {
            new() { Label = "Event", IsOn = _selectedEventTypes.Contains(EventMapType.Event),
                    Value = EventMapType.Event, AccentColor = EventCatalogConfig.EventColor },
            new() { Label = "PvP", IsOn = _selectedEventTypes.Contains(EventMapType.PvP),
                    Value = EventMapType.PvP, AccentColor = EventCatalogConfig.PvPColor },
        };

        MultiToggleFilterPopup.Show(_canvasRoot, anchor, Localization.Get("filter.type.title"), options, _eventTypesShowAll,
            onShowAllSelected: () =>
            {
                _eventTypesShowAll = true;
                _selectedEventTypes.Clear();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                var type = (EventMapType)option.Value;
                _eventTypesShowAll = false;
                if (option.IsOn) _selectedEventTypes.Add(type);
                else
                {
                    _selectedEventTypes.Remove(type);
                    if (_selectedEventTypes.Count == 0) _eventTypesShowAll = true;
                }
                RefreshVisibility();
            });
    }

    private void ShowSetFilterPopup<T>(RectTransform anchor, string title, SetFilter<T> filter,
        IEnumerable<(string Label, T Value, Color32? Color)> values, Action afterChange = null)
    {
        var options = values.Select(v => new FilterOption
        {
            Label = v.Label,
            IsOn = filter.Selected.Contains(v.Value),
            Value = v.Value,
            AccentColor = v.Color
        }).ToList();

        MultiToggleFilterPopup.Show(_canvasRoot, anchor, title, options, filter.ShowAll,
            onShowAllSelected: () =>
            {
                filter.Reset();
                afterChange?.Invoke();
                RefreshVisibility();
            },
            onOptionToggled: option =>
            {
                var value = (T)option.Value;
                filter.ShowAll = false;
                if (option.IsOn) filter.Selected.Add(value);
                else
                {
                    filter.Selected.Remove(value);
                    if (filter.Selected.Count == 0) filter.ShowAll = true;
                }
                afterChange?.Invoke();
                RefreshVisibility();
            });
    }

    private void OpenServerStatusFilterPopup(RectTransform anchor) =>
        ShowSetFilterPopup(anchor, Localization.Get("filter.status.title"), _serverStatusFilter, new[]
        {
            (Localization.Get("filter.status.not_downloaded"), MapStatus.NotDownloaded, (Color32?)new Color32(180, 60, 60, 255)),
            (Localization.Get("filter.status.installed"), MapStatus.Installed, (Color32?)new Color32(200, 160, 40, 255)),
            (Localization.Get("filter.status.running"), MapStatus.Running, (Color32?)new Color32(50, 160, 70, 255))
        });

    private void OpenSourceFilterPopup(RectTransform anchor)
    {
        ShowSetFilterPopup(anchor, Localization.Get("filter.editor.title"), _sourceFilter,
            new[] { ArchitectSource.NewArchitect, ArchitectSource.LegacyArchitect, ArchitectSource.Silksong }
                .Select(src => (ArchitectServerConfig.GetSourceLabel(src), src,
                                (Color32?)ArchitectServerConfig.GetSourceColor(src))),
            afterChange: () => _showSilksong = _sourceFilter.Selected.Contains(ArchitectSource.Silksong));
    }

    private void OpenDifficultyFilterPopup(RectTransform anchor) =>
        ShowSetFilterPopup(anchor, Localization.Get("filter.difficulty.title"), _difficultyFilter,
            new[] { ServerDifficulty.Easy, ServerDifficulty.Medium, ServerDifficulty.Hard, ServerDifficulty.Extreme, ServerDifficulty.None }
                .Select(d => (d == ServerDifficulty.None ? Localization.Get("server.not_set") : ArchitectServerConfig.GetDifficultyLabel(d),
                              d, (Color32?)ArchitectServerConfig.GetDifficultyColor(d))));

    private void OpenDurationFilterPopup(RectTransform anchor) =>
        ShowSetFilterPopup(anchor, Localization.Get("filter.duration.title"), _durationFilter,
            new[] { ServerDuration.Tiny, ServerDuration.Short, ServerDuration.Medium, ServerDuration.Long, ServerDuration.None }
                .Select(d => (d == ServerDuration.None ? Localization.Get("server.not_set") : ArchitectServerConfig.GetDurationLabel(d),
                              d, (Color32?)ArchitectServerConfig.GetDurationColor(d))));

    private void OpenServerTagsFilterPopup(RectTransform anchor) =>
        ShowSetFilterPopup(anchor, Localization.Get("filter.tags.title"), _serverTagFilter,
            ((ServerTag[])Enum.GetValues(typeof(ServerTag))).Select(t => (t.ToString(), t, (Color32?)ArchitectServerConfig.GetTagColor(t))));

    private string SortButtonLabel() => _serverSort == ServerSortMode.UploadDate
        ? Localization.Get("list.sort_by_date")
        : Localization.Get("list.sort_by_downloads");

    private void ToggleServerSort()
    {
        _serverSort = _serverSort == ServerSortMode.UploadDate ? ServerSortMode.Downloads : ServerSortMode.UploadDate;
        if (_sortButtonText != null)
        {
            _sortButtonText.text = SortButtonLabel();
            if (_sortButtonRect != null)
                _sortButtonRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(_sortButtonText, min: 110f), FavoritesButtonSize);
        }
        RebuildButtons();
        RefreshVisibility();
    }

    private static TriFilterMode NextMode(TriFilterMode mode) => (TriFilterMode)(((int)mode + 1) % 3);

    private static bool MarkPasses(TriFilterMode mode, bool has) => mode == TriFilterMode.Ignore || (mode == TriFilterMode.Include) == has;

    private void UpdateMarkFilterColors()
    {
        foreach (var (image, text, getMode, onColor, glyph) in _markFilterButtons)
        {
            var mode = getMode();
            if (image != null)
                image.color = mode switch
                {
                    TriFilterMode.Include => onColor,
                    TriFilterMode.Exclude => new Color(0.42f, 0.10f, 0.10f, 1f),
                    _ => new Color(0.157f, 0.145f, 0.180f, 1f)
                };
            if (text != null) text.text = mode == TriFilterMode.Exclude ? "× " + glyph : glyph;
        }
    }

    private bool PassesServerFilters(MapRow entry)
    {
        if (entry.Catalog != MapCatalogKind.ArchitectServer) return true;

        if (!_sourceFilter.Passes(entry.ServerSource)) return false;

        bool metadataFilterActive = !_difficultyFilter.ShowAll || !_durationFilter.ShowAll || !_serverTagFilter.ShowAll;
        if (metadataFilterActive && !entry.HasServerMetadata) return false;

        if (!_difficultyFilter.Passes(entry.Difficulty)) return false;
        if (!_durationFilter.Passes(entry.Duration)) return false;
        if (!_serverTagFilter.ShowAll && !entry.ServerTags.Any(_serverTagFilter.Selected.Contains)) return false;
        if (!_serverStatusFilter.Passes(GetMapStatus(entry))) return false;

        return true;
    }

    private bool PassesFilters(MapRow entry)
    {
        if (entry.HiddenDuplicate) return false;

        if (entry.Catalog == MapCatalogKind.ArchitectServer &&
            entry.ServerSource == ArchitectSource.Silksong && !_showSilksong)
            return false;

        if (!MatchesSearch(entry))
            return false;

        if (_favoritesOnly && !FavoritesStore.IsFavorite(entry))
            return false;

        if (!MarkPasses(_seenMode, MapMarksStore.IsSeen(entry))) return false;
        if (_likedOnly && MapMarksStore.GetReaction(entry) != MapReaction.Liked) return false;
        if (_dislikedOnly && MapMarksStore.GetReaction(entry) != MapReaction.Disliked) return false;

        if (!PassesServerFilters(entry)) return false;

        if (!_eventTypesShowAll && !_selectedEventTypes.Contains(entry.EventType))
            return false;

        if (!_starsShowAll && !_selectedStars.Contains(Mathf.Clamp(entry.Stars, 0, 5)))
            return false;
        if (!_editorsShowAll && (entry.Editors == null || !entry.Editors.Any(e => _selectedEditors.Contains(e))))
            return false;
        if (!_verificationShowAll && !_selectedVerification.Contains(entry.Verified))
            return false;
        if (!_tagsShowAll && (entry.Tags == null || !entry.Tags.Any(t => _selectedTags.Contains(t))))
            return false;
        if (!_statusesShowAll)
        {
            var currentStatus = GetMapStatus(entry);
            bool passesStatus = false;
            foreach (var selectedStatus in _selectedStatuses)
            {
                if (selectedStatus == currentStatus)
                {
                    passesStatus = true;
                    break;
                }
                if (selectedStatus == MapStatus.Installed && currentStatus == MapStatus.Running)
                {
                    passesStatus = true;
                    break;
                }
            }
            if (!passesStatus) return false;
        }
        return true;
    }

    private void RefreshVisibility()
    {
        _visibleButtonIndices = new List<int>();
        for (int i = 0; i < _buttonGos.Count; i++)
        {
            var entry = _entries[_displayEntryIndices[i]];
            bool visible = PassesFilters(entry);
            _buttonGos[i].SetActive(visible);
            if (visible) _visibleButtonIndices.Add(i);
        }
        foreach (var kvp in _headerGosByLeague)
        {
            League league = kvp.Key;
            bool anyVisible = _entries.Where(e => e.League == league).Any(PassesFilters);
            foreach (var go in kvp.Value) go.SetActive(anyVisible);
        }
        foreach (var kvp in _headerGosByGroup)
        {
            string group = kvp.Key;
            bool anyVisible = _entries.Where(e => GetServerGroupKey(e) == group).Any(PassesFilters);
            foreach (var go in kvp.Value) go.SetActive(anyVisible);
        }
        if (_selectedButtonIndex < 0 || !_visibleButtonIndices.Contains(_selectedButtonIndex))
        {
            SelectByButtonIndex(_visibleButtonIndices.Count > 0 ? _visibleButtonIndices[0] : -1);
        }
    }

    private void SelectByButtonIndex(int buttonIndex)
    {
        if (_selectedButtonIndex >= 0 && _selectedButtonIndex < _buttonImages.Count)
            ApplyButtonColor(_selectedButtonIndex, selected: false);
        _selectedButtonIndex = buttonIndex;
        if (_selectedButtonIndex >= 0 && _selectedButtonIndex < _buttonImages.Count)
            ApplyButtonColor(_selectedButtonIndex, selected: true);
        SelectionChanged?.Invoke(SelectedMap);
    }

    private void ApplyButtonColor(int buttonIndex, bool selected)
    {
        var entry = _entries[_displayEntryIndices[buttonIndex]];
        _buttonImages[buttonIndex].color = selected
            ? DarkenColor(entry.CellColor) : (Color)entry.CellColor;
    }

    private void MoveSelection(int direction)
    {
        if (_visibleButtonIndices.Count == 0) return;
        int pos = _visibleButtonIndices.IndexOf(_selectedButtonIndex);
        if (pos < 0) pos = 0;
        int nextPos = Mathf.Clamp(pos + direction, 0, _visibleButtonIndices.Count - 1);
        if (nextPos == pos) return;
        SelectByButtonIndex(_visibleButtonIndices[nextPos]);
        EnsureSelectionVisible();
    }

    private void EnsureSelectionVisible()
    {
        if (_scrollRect == null || _listContent == null) return;
        if (_selectedButtonIndex < 0 || _selectedButtonIndex >= _buttonGos.Count) return;
        Canvas.ForceUpdateCanvases();
        var buttonRect = (RectTransform)_buttonGos[_selectedButtonIndex].transform;
        float viewportHeight = _scrollRect.viewport.rect.height;
        float margin = viewportHeight * AutoScrollMarginFraction;
        float buttonCenterY = buttonRect.localPosition.y;
        float buttonTopY = buttonCenterY + buttonRect.rect.yMax;
        float buttonBottomY = buttonCenterY + buttonRect.rect.yMin;
        float scrollOffset = _listContent.anchoredPosition.y;
        float bandTop = -scrollOffset - margin;
        float bandBottom = -(scrollOffset + viewportHeight) + margin;
        float newOffset = scrollOffset;
        if (buttonTopY > bandTop) newOffset = -buttonTopY - margin;
        else if (buttonBottomY < bandBottom) newOffset = margin - buttonBottomY - viewportHeight;
        float maxScroll = Mathf.Max(0f, _listContent.rect.height - viewportHeight);
        newOffset = Mathf.Clamp(newOffset, 0f, maxScroll);
        _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, newOffset);
    }

    private void CreateOverlay()
    {
        if (_overlayGo != null) return;
        _overlayGo = new GameObject("GlobalListAtlas_Overlay", typeof(RectTransform), typeof(Image));
        UnityEngine.Object.DontDestroyOnLoad(_overlayGo);
        var canvas = _overlayGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;
        _overlayGo.AddComponent<GraphicRaycaster>();
        var image = _overlayGo.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.65f);
        image.raycastTarget = true;
        var rect = _overlayGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
    }

    private void DestroyOverlay()
    {
        if (_overlayGo != null)
        {
            UnityEngine.Object.Destroy(_overlayGo);
            _overlayGo = null;
        }
    }
}