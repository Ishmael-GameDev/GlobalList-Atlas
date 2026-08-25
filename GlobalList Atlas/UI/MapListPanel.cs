using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Maps;
using GlobalListAtlas.Net;
using UnityEngine;
using UnityEngine.UI;

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
    private const float RowGap = 4f;
    private const float TopReservedHeight =
        StatusBarHeight + RowGap + FilterRowHeight + RowGap + CatalogProgressBarHeight + RowGap;
    private const float CatalogRetryIntervalSeconds = 3f;
    private const float StatusBarUpdateIntervalSeconds = 0.5f;

    private int _heldDirection = 0;
    private float _heldDuration = 0f;
    private float _timeSinceLastRepeat = 0f;
    private GameObject _canvasGo;
    private RectTransform _canvasRoot;
    private RectTransform _listContent;
    private ScrollRect _scrollRect;
    private Text _statusBarText;
    private float _statusBarTimer;
    private GameObject _catalogProgressGo;
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
        OpenStateChanged?.Invoke(true);
        _ = LoadAndPopulateAsync();
    }

    public void Close()
    {
        _isOpen = false;
        if (_canvasGo != null)
            _canvasGo.SetActive(false);
        DestroyOverlay();
        OpenStateChanged?.Invoke(false);
    }

    private async Task LoadAndPopulateAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        SetCatalogProgressVisible(true);
        SetCatalogProgressIndeterminate(Localization.Get("list.loading_catalog"));
        try
        {
            var manager = GlobalListAtlasMod.Instance?.DownloadManager;
            if (manager == null)
            {
                _loadError = Localization.Get("error.manager_unavailable");
                return;
            }
            var allMaps = await manager.GetAllMapsAsync();
            _entries.Clear();
            _entries.AddRange(allMaps);
            RebuildButtons();
            RefreshVisibility();
            if (_canvasGo != null)
            {
                _canvasGo.SetActive(_isOpen);
                OpenStateChanged?.Invoke(_isOpen);
            }
            _loadError = null;
        }
        catch (Exception e)
        {
            _loadError = e.Message;
            Modding.Logger.Log($"[Список карт] Не удалось загрузить каталог: {e.Message}. " +
                                $"Повтор через {CatalogRetryIntervalSeconds:F0} с...");
        }
        finally
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

    private void ScheduleRetry()
    {
        if (_retryScheduled) return;
        _retryScheduled = true;
        _ = RetryLoopAsync();
    }

    private async Task RetryLoopAsync()
    {
        try
        {
            while (_loadError != null && _isOpen)
            {
                await Task.Delay(TimeSpan.FromSeconds(CatalogRetryIntervalSeconds));
                if (!_isOpen) break;
                await LoadAndPopulateAsync();
            }
        }
        finally
        {
            _retryScheduled = false;
        }
    }

    private void OnNetworkOnlineStateChanged(bool isOnline)
    {
        if (isOnline && _loadError != null && _isOpen && !_isLoading)
            _ = LoadAndPopulateAsync();
    }

    private void OnCatalogLoadProgressChanged(long received, long? total)
    {
        if (!_isLoading) return;
        if (total.HasValue && total.Value > 0)
        {
            float fraction = Mathf.Clamp01((float)received / total.Value);
            if (_catalogProgressFill != null)
                ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(fraction, 1);
            if (_catalogProgressText != null)
                _catalogProgressText.text = Localization.Get("catalog.loading_percent",
                    fraction * 100f, FormatBytes(received), FormatBytes(total.Value));
        }
        else
        {
            if (_catalogProgressFill != null)
                ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(1, 1);
            if (_catalogProgressText != null)
                _catalogProgressText.text = Localization.Get("catalog.loading_bytes", FormatBytes(received));
        }
    }

    private void SetCatalogProgressVisible(bool visible)
    {
        if (_catalogProgressGo != null)
            _catalogProgressGo.SetActive(visible);
    }

    private void SetCatalogProgressIndeterminate(string message)
    {
        if (_catalogProgressFill != null)
            ((RectTransform)_catalogProgressFill.transform).anchorMax = new Vector2(0, 1);
        if (_catalogProgressText != null)
            _catalogProgressText.text = message;
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
        if (_isOpen && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }
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
        UpdateStatusBar();
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

        var filterRow = new GameObject("FilterRow", typeof(RectTransform));
        filterRow.transform.SetParent(panel, false);
        var filterRowRect = (RectTransform)filterRow.transform;
        filterRowRect.anchorMin = new Vector2(0, 1);
        filterRowRect.anchorMax = new Vector2(1, 1);
        filterRowRect.pivot = new Vector2(0.5f, 1);
        filterRowRect.sizeDelta = new Vector2(0, FilterRowHeight);
        filterRowRect.anchoredPosition = new Vector2(0, -(StatusBarHeight + RowGap));
        const float filterButtonGap = 4f;
        const int filterSlotCount = 5;
        CreateFilterButton(filterRowRect, "StarsFilterButton",
            Localization.Get("list.filter_stars"), 0, filterSlotCount, filterButtonGap, OpenStarsFilterPopup);
        CreateFilterButton(filterRowRect, "EditorFilterButton",
            Localization.Get("list.filter_editor"), 1, filterSlotCount, filterButtonGap, OpenEditorFilterPopup);
        CreateFilterButton(filterRowRect, "TagsFilterButton",
            Localization.Get("list.filter_tags"), 2, filterSlotCount, filterButtonGap, OpenTagsFilterPopup);
        CreateFilterButton(filterRowRect, "VerifiedFilterButton",
            Localization.Get("list.filter_verified"), 3, filterSlotCount, filterButtonGap, OpenVerificationFilterPopup);
        CreateFilterButton(filterRowRect, "StatusFilterButton",
            Localization.Get("list.filter_status"), 4, filterSlotCount, filterButtonGap, OpenStatusFilterPopup);

        var (progressGo, progressFill, progressText) = UIFactory.CreateProgressBar(panel, "CatalogProgressBar");
        var progressRect = (RectTransform)progressGo.transform;
        progressRect.anchorMin = new Vector2(0, 1);
        progressRect.anchorMax = new Vector2(1, 1);
        progressRect.pivot = new Vector2(0.5f, 1);
        progressRect.sizeDelta = new Vector2(0, CatalogProgressBarHeight);
        progressRect.anchoredPosition = new Vector2(0, -(StatusBarHeight + RowGap + FilterRowHeight + RowGap));
        progressText.fontSize = 13;
        _catalogProgressGo = progressGo;
        _catalogProgressFill = progressFill;
        _catalogProgressText = progressText;
        _catalogProgressGo.SetActive(false);

        var (content, scrollRect) = UIFactory.CreateVerticalScrollList(panel, "MapListScroll");
        _scrollRect = scrollRect;
        var scrollRootRect = (RectTransform)content.parent.parent;
        scrollRootRect.anchorMin = new Vector2(0, 0);
        scrollRootRect.anchorMax = new Vector2(1, 1);
        scrollRootRect.offsetMin = new Vector2(0, 0);
        scrollRootRect.offsetMax = new Vector2(0, -TopReservedHeight);
        _listContent = content;

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
        //UpdateLanguageButtons(); Бан рекурсии
    }

    private void RebuildButtons()
    {
        foreach (var go in _rowGos) Destroy(go);
        _rowGos.Clear();
        _buttonGos.Clear();
        _buttonImages.Clear();
        _displayEntryIndices.Clear();
        _headerGosByLeague.Clear();
        var orderedIndices = Enumerable.Range(0, _entries.Count)
            .OrderBy(i => LeagueConfig.GetOrder(_entries[i].League))
            .ThenBy(i => i)
            .ToList();
        League? currentLeague = null;
        foreach (int originalIndex in orderedIndices)
        {
            var entry = _entries[originalIndex];
            if (currentLeague == null || entry.League != currentLeague)
            {
                currentLeague = entry.League;
                var headerGos = CreateLeagueHeader(entry.League);
                _rowGos.AddRange(headerGos);
                if (!_headerGosByLeague.TryGetValue(entry.League, out var list))
                {
                    list = new List<GameObject>();
                    _headerGosByLeague[entry.League] = list;
                }
                list.AddRange(headerGos);
            }
            string label = $"{entry.Name}   {StarsToString(entry.Stars)}";
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

    private GameObject[] CreateLeagueHeader(League league)
    {
        var spacerGo = new GameObject($"Spacer_{league}", typeof(RectTransform));
        spacerGo.transform.SetParent(_listContent, false);
        ((RectTransform)spacerGo.transform).sizeDelta = new Vector2(0, 14);
        var text = UIFactory.CreateText(_listContent, $"Header_{league}",
            LeagueConfig.GetLocalizedLabel(league), 16, TextAnchor.MiddleLeft);
        ((RectTransform)text.transform).sizeDelta = new Vector2(0, 26);
        return new[] { spacerGo, text.gameObject };
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

    private static void CreateFilterButton(
        RectTransform parent, string name, string label,
        int slotIndex, int totalSlots, float gap, Action<RectTransform> onClick)
    {
        var (go, button, _, _) = UIFactory.CreateButton(parent, name, label, 15);
        var rect = (RectTransform)go.transform;
        float slotWidth = 1f / totalSlots;
        rect.anchorMin = new Vector2(slotIndex * slotWidth, 0);
        rect.anchorMax = new Vector2((slotIndex + 1) * slotWidth, 1);
        float leftGap = slotIndex == 0 ? 0f : gap / 2f;
        float rightGap = slotIndex == totalSlots - 1 ? 0f : gap / 2f;
        rect.offsetMin = new Vector2(leftGap, 0);
        rect.offsetMax = new Vector2(-rightGap, 0);
        button.onClick.AddListener(() => onClick(rect));
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

    private bool PassesFilters(MapRow entry)
    {
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
                // Запущенная карта считается и установленной
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