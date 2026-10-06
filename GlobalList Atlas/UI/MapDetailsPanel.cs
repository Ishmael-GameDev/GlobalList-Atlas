using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using GlobalListAtlas.Integrations;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;
using GlobalListAtlas.Util;
using GlobalListAtlas.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

public class MapDetailsPanel : MonoBehaviour
{
    private const float PanelAnchorMinX = MapListPanel.PanelWidthFraction;
    private const float PanelAnchorMaxX = MapListPanel.PanelWidthFraction * 3f;
    private const float PanelRightReduction = 500f;

    private static readonly Color PositiveBadgeColor = new(0.129f, 0.302f, 0.176f, 0.85f);
    private static readonly Color NegativeBadgeColor = new(0.310f, 0.145f, 0.153f, 0.85f);
    private static readonly Color NeutralBadgeColor = new(0.165f, 0.176f, 0.212f, 0.85f);
    private static readonly Color NotificationBg = new(0.278f, 0.227f, 0.086f, 0.85f);
    private static readonly Color MutedGray = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color GoodGreen = new(0.6f, 1f, 0.6f);
    private static readonly Color BadRed = new(1f, 0.6f, 0.6f);
    private static readonly Color TxtNoticeYellow = new(1f, 0.85f, 0.4f);
    private static readonly Color DownloadedYellowBg = new(0.55f, 0.47f, 0.12f, 1f);
    private static readonly Color LaunchedGreenBg = new(0.16f, 0.45f, 0.2f, 1f);
    private static readonly Color DarkButtonBg = new(0.14f, 0.14f, 0.18f, 1f);
    private static readonly Color FavoriteBg = new(0.478f, 0.192f, 0.255f, 1f); // как у сердечка в списке
    private static readonly Color RestartButtonBg = new(0.639f, 0.373f, 0.098f, 1f);   // оранжевый
    private static readonly Color RestartButtonFill = new(0.949f, 0.639f, 0.243f, 1f);
    private static readonly Color ReinstallRedBg = new(0.435f, 0.220f, 0.192f, 1f);   // приглушённый кирпич
    private static readonly Color DeleteCrimsonBg = new(0.455f, 0.086f, 0.180f, 1f); // багровый
    private static readonly Color DeleteCrimsonFill = new(0.796f, 0.243f, 0.373f, 1f);
    private static readonly Color PrimaryActionBg = new(0.239f, 0.475f, 0.455f, 1f);  // ведущие действия
    private static readonly Color ModsActionBg = new(0.255f, 0.400f, 0.549f, 1f);

    private const float ReinstallButtonHeight = 30f;
    private const float HeaderRowHeight = 30f;
    private const float CloseButtonReserve = 46f;
    private const float HeaderButtonGap = 6f;
    private GameObject _openSheetGo;
    private GameObject _vpnButtonGo;
    private static bool IsDevMode => GlobalListAtlasMod.Instance?.Settings.IsDevToolsUnlocked == true;
    private const float ReinstallButtonWidthFraction = 0.5f;

    private Button _favoriteButton;
    private Image _favoriteImage;
    private Text _favoriteText;
    private Button _openSheetButton;

    private bool _languageSubscribed;

    private GameObject _canvasGo;
    private RectTransform _canvasRoot;
    private RectTransform _content;
    private bool _subscribed;
    private MapRow _currentMap;
    private readonly List<GameObject> _contentGos = new();
    private Button _downloadButton;
    private Text _downloadButtonText;
    private bool _launchInProgress;
    private Image _downloadProgressFill;
    private Text _downloadProgressText;
    private Button _installModsButton;
    private Text _installModsButtonText;
    private Button _reinstallButton;
    private Text _reinstallButtonText;
    private Button _installPublicModsButton;
    private Text _installPublicModsButtonText;
    private bool _installPublicModsInProgress;
    private bool _editorActionInProgress;
    private bool _manifestsRequested;
    private readonly HashSet<string> _sizeRequested = new();
    private readonly HashSet<string> _reinstallingMapKeys = new();

    private bool _autoSaveSubscribed;
    private void Update()
    {
        UpdateVpnButtonVisibility();
        if (!_subscribed && MapListPanel.Instance != null)
        {
            MapListPanel.Instance.SelectionChanged += OnSelectionChanged;
            MapListPanel.Instance.OpenStateChanged += OnOpenStateChanged;
            _subscribed = true;
            OnOpenStateChanged(MapListPanel.Instance.IsOpen);
            OnSelectionChanged(MapListPanel.Instance.SelectedMap);
        }
        if (!_progressSubscribed && GlobalListAtlasMod.Instance?.DownloadManager != null)
        {
            GlobalListAtlasMod.Instance.DownloadManager.MapDownloadProgressChanged += OnMapDownloadProgressChanged;
            _progressSubscribed = true;
        }
        if (!_languageSubscribed && GlobalListAtlasMod.Instance != null)
        {
            GlobalListAtlasMod.Instance.LanguageChanged += OnLanguageChanged;
            _languageSubscribed = true;
        }
        if (!_autoSaveSubscribed && GlobalListAtlasMod.Instance != null)
        {
            GlobalListAtlasMod.Instance.AutoSaveToggled += OnAutoSaveToggled;
            _autoSaveSubscribed = true;
        }
    }
    private void OnAutoSaveToggled(bool enabled)
    {
        if (_canvasGo == null || !_canvasGo.activeSelf) return;

        string key = enabled ? "autosave.enabled" : "autosave.disabled";

        Color bgColor = enabled
            ? PositiveBadgeColor
            : new Color(0.65f, 0.2f, 0.2f, 0.95f);

        ShowNotification(Localization.Get(key), bgColor);
    }
    public void RefreshLanguage()
    {
        if (_currentMap != null)
        {
            RefreshContent();
        }
    }
    private bool _progressSubscribed;
    private void OnLanguageChanged()
    {
        RefreshAllUI();
    }

    private void RefreshAllUI()
    {
        if (_canvasGo != null)
        {
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _content = null;
            _downloadButton = null;
            _installModsButton = null;
            _reinstallButton = null;
            _installPublicModsButton = null;
        }

        BuildCanvasIfNeeded();
        _canvasGo.SetActive(MapListPanel.Instance?.IsOpen ?? false);

        if (_currentMap != null)
        {
            RefreshContent();
        }
    }
    private void OnMapDownloadProgressChanged(MapRow map, long received, long? total)
    {
        if (!IsSameMap(_currentMap, map)) return;
        UpdateDownloadProgressBar(received, total);
    }

    private void UpdateDownloadProgressBar(long received, long? total)
    {
        if (_downloadProgressFill == null) return;
        var rect = (RectTransform)_downloadProgressFill.transform;
        if (total.HasValue && total.Value > 0)
        {
            float fraction = Mathf.Clamp01((float)received / total.Value);
            rect.anchorMax = new Vector2(fraction, 1);
            if (_downloadProgressText != null)
                _downloadProgressText.text = $"{fraction * 100f:F0}%  ({FormatBytes(received)} / {FormatBytes(total.Value)})";
        }
        else
        {
            rect.anchorMax = new Vector2(1, 1);
            if (_downloadProgressText != null)
                _downloadProgressText.text = FormatBytes(received);
        }
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1
            ? $"{mb:F1} {Localization.Get("format.mb")}"
            : $"{bytes / 1024.0:F0} {Localization.Get("format.kb")}";
    }

    private void OnDestroy()
    {
        if (_subscribed && MapListPanel.Instance != null)
        {
            MapListPanel.Instance.SelectionChanged -= OnSelectionChanged;
            MapListPanel.Instance.OpenStateChanged -= OnOpenStateChanged;
        }
        if (_progressSubscribed && GlobalListAtlasMod.Instance?.DownloadManager != null)
            GlobalListAtlasMod.Instance.DownloadManager.MapDownloadProgressChanged -= OnMapDownloadProgressChanged;
        if (_languageSubscribed && GlobalListAtlasMod.Instance != null)
            GlobalListAtlasMod.Instance.LanguageChanged -= OnLanguageChanged;
        if (_autoSaveSubscribed && GlobalListAtlasMod.Instance != null)
            GlobalListAtlasMod.Instance.AutoSaveToggled -= OnAutoSaveToggled;
    }

    private void OnOpenStateChanged(bool isOpen)
    {
        BuildCanvasIfNeeded();
        _canvasGo.SetActive(isOpen);
    }

    private void OnSelectionChanged(MapRow map)
    {
        _currentMap = map;
        _launchInProgress = false;
        _installPublicModsInProgress = false;
        RefreshContent();
    }

    private void BuildCanvasIfNeeded()
    {
        if (_canvasGo != null) return;
        UIFactory.CreateRootCanvas("MapDetailsPanelCanvas", out _canvasGo);
        _canvasRoot = (RectTransform)_canvasGo.transform;
        var panel = UIFactory.CreatePanel(_canvasRoot, "MapDetailsPanel", UIFactory.PanelBg);
        panel.anchorMin = new Vector2(PanelAnchorMinX, 0);
        panel.anchorMax = new Vector2(PanelAnchorMaxX, 1);
        panel.offsetMin = new Vector2(MapListPanel.ScreenPadding + MapListPanel.PanelHorizontalShift - 35f, MapListPanel.ScreenPadding);
        panel.offsetMax = new Vector2(
            -MapListPanel.ScreenPadding + MapListPanel.PanelHorizontalShift - PanelRightReduction,
            -MapListPanel.ScreenPadding);
        var headerRow = new GameObject("HeaderActions", typeof(RectTransform));
        headerRow.transform.SetParent(panel, false);
        var headerRect = (RectTransform)headerRow.transform;
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.sizeDelta = new Vector2(-(CloseButtonReserve + 8f), HeaderRowHeight);
        headerRect.anchoredPosition = new Vector2(-(CloseButtonReserve + 8f) / 2f, -6f);

        var (favGo, favButton, favImage, favText) = UIFactory.CreateButton(
            headerRow.transform, "FavoriteButton", Localization.Get("favorite.add"), 15);
        favText.alignment = TextAnchor.MiddleCenter;
        favText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UIFactory.AddOutline(favGo);
        var favRect = (RectTransform)favGo.transform;
        favRect.anchorMin = new Vector2(0, 0);
        favRect.anchorMax = new Vector2(0, 1);
        favRect.pivot = new Vector2(0, 0.5f);
        favRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(favText, min: 120f), 0);
        favRect.anchoredPosition = new Vector2(8f, 0);
        _favoriteButton = favButton;
        _favoriteImage = favImage;
        _favoriteText = favText;
        favButton.onClick.AddListener(OnFavoriteClicked);

        var (linkGo, linkButton, linkImage, linkText) = UIFactory.CreateButton(
            headerRow.transform, "OpenInSheetButton", Localization.Get("button.open_in_sheet"), 15);
        linkText.alignment = TextAnchor.MiddleCenter;
        linkText.horizontalOverflow = HorizontalWrapMode.Overflow;
        linkImage.color = DarkButtonBg;
        linkText.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        UIFactory.AddOutline(linkGo);
        var linkRect = (RectTransform)linkGo.transform;
        linkRect.anchorMin = new Vector2(0, 0);
        linkRect.anchorMax = new Vector2(0, 1);
        linkRect.pivot = new Vector2(0, 0.5f);
        linkRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(linkText, min: 150f), 0);
        linkRect.anchoredPosition = new Vector2(favRect.sizeDelta.x + 16f, 0);
        _openSheetButton = linkButton;
        _openSheetGo = linkGo;
        linkButton.onClick.AddListener(OnOpenSheetClicked);

        var (content, detailsScroll) = UIFactory.CreateVerticalScrollList(panel, "DetailsScroll");
        _detailsScroll = detailsScroll;
        var scrollRoot = (RectTransform)content.parent.parent;
        scrollRoot.offsetMax = new Vector2(scrollRoot.offsetMax.x, -(HeaderRowHeight + 10f));
        _content = content;

        var closeBtnGo = new GameObject("CloseMenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
        closeBtnGo.transform.SetParent(panel, false);
        var closeBtnRect = closeBtnGo.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1, 1);
        closeBtnRect.anchorMax = new Vector2(1, 1);
        closeBtnRect.pivot = new Vector2(1, 1);
        closeBtnRect.sizeDelta = new Vector2(40, 40);
        closeBtnRect.anchoredPosition = new Vector2(-6, -6);
        var closeBtnBg = closeBtnGo.GetComponent<Image>();
        closeBtnBg.color = new Color(0.7f, 0.15f, 0.15f, 0.95f);
        closeBtnBg.raycastTarget = true;
        var closeBtn = closeBtnGo.GetComponent<Button>();
        closeBtn.transition = Selectable.Transition.ColorTint;
        var closeTextGo = new GameObject("CloseX", typeof(RectTransform), typeof(Text));
        closeTextGo.transform.SetParent(closeBtnRect, false);
        var closeText = closeTextGo.GetComponent<Text>();
        closeText.font = UIFactory.DefaultFont;
        closeText.fontSize = 28;
        closeText.fontStyle = FontStyle.Bold;
        closeText.alignment = TextAnchor.MiddleCenter;
        closeText.text = "✕";
        closeText.color = Color.white;
        closeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        closeText.verticalOverflow = VerticalWrapMode.Overflow;
        var closeTextRect = closeTextGo.GetComponent<RectTransform>();
        closeTextRect.anchorMin = Vector2.zero;
        closeTextRect.anchorMax = Vector2.one;
        closeTextRect.offsetMin = Vector2.zero;
        closeTextRect.offsetMax = Vector2.zero;
        closeBtn.onClick.AddListener(() => MapListPanel.Instance?.Close());
        var (helpGo, helpButton, helpImage, helpText) = UIFactory.CreateButton(panel, "HelpButton", "?", 20);
        helpText.alignment = TextAnchor.MiddleCenter;
        var helpTextRect = (RectTransform)helpText.transform;
        helpTextRect.offsetMin = new Vector2(0, helpTextRect.offsetMin.y);
        helpTextRect.offsetMax = new Vector2(0, helpTextRect.offsetMax.y);
        helpImage.color = DarkButtonBg;
        UIFactory.AddOutline(helpGo);
        var helpRect = (RectTransform)helpGo.transform;
        helpRect.anchorMin = new Vector2(1, 1);
        helpRect.anchorMax = new Vector2(1, 1);
        helpRect.pivot = new Vector2(1, 1);
        helpRect.sizeDelta = new Vector2(34, 34);
        helpRect.anchoredPosition = new Vector2(-(CloseButtonReserve + HeaderButtonGap), -9f);
        helpButton.onClick.AddListener(() => HelpPopup.Show());

        var (vpnGo, vpnButton, vpnImage, vpnText) = UIFactory.CreateButton(panel, "VpnButton", "VPN", 13);
        vpnText.alignment = TextAnchor.MiddleCenter;
        vpnImage.color = DarkButtonBg;
        UIFactory.AddOutline(vpnGo);
        var vpnRect = (RectTransform)vpnGo.transform;
        vpnRect.anchorMin = new Vector2(1, 1);
        vpnRect.anchorMax = new Vector2(1, 1);
        vpnRect.pivot = new Vector2(1, 1);
        float vpnWidth = UIFactory.MeasureButtonWidth(vpnText, min: 34f);
        vpnRect.sizeDelta = new Vector2(vpnWidth, 34);
        vpnRect.anchoredPosition = new Vector2(-(CloseButtonReserve + HeaderButtonGap + 34f + HeaderButtonGap), -9f);
        vpnButton.onClick.AddListener(() => HelpPopup.ShowVpnGuide());
        vpnGo.SetActive(IsDevMode);
        _vpnButtonGo = vpnGo;
    }

    private void UpdateVpnButtonVisibility()
    {
        if (_vpnButtonGo == null) return;
        bool visible = IsDevMode;
        if (_vpnButtonGo.activeSelf != visible) _vpnButtonGo.SetActive(visible);
    }

    private void RefreshContent()
    {
        if (_content == null) return;

        UpdateHeaderButtons();

        foreach (var go in _contentGos) Destroy(go);
        _contentGos.Clear();
        _downloadButton = null;
        _installModsButton = null;
        _reinstallButton = null;
        _installPublicModsButton = null;

        if (_currentMap == null)
        {
            AddText(Localization.Get("list.select_map"), 20, TextAnchor.MiddleLeft, Color.gray, 32);
            AddSpacer(10);
            AddIntegrationsOverview();
            return;
        }

        var map = _currentMap;
        var title = map.Catalog == MapCatalogKind.ArchitectServer
            ? AddArchitectTitleRow(map)
            : AddWrappedText(map.Name, 26, map.CellColor);
        title.fontStyle = FontStyle.Bold;

        string author = string.IsNullOrWhiteSpace(map.Creator)
            ? $"<color=#AAAAAA>{Localization.Get("panel.author_unknown")}</color>"
            : map.Creator;
        AddWrappedText($"{Localization.Get("panel.author")}: {author}", 18, Color.white);

        var editorItems = map.Catalog == MapCatalogKind.ArchitectServer
            ? new List<(string, Color32)> { (ArchitectServerConfig.GetSourceLabel(map.ServerSource), ArchitectServerConfig.GetSourceColor(map.ServerSource)) }
            : (map.Editors ?? new List<MapEditor>())
                .Select(e => (EditorConfig.GetLabel(e), (Color32)EditorConfig.GetColor(e)))
                .ToList();
        string editorsRich = editorItems.Count > 0
            ? BuildColoredList(editorItems)
            : $"<color=#AAAAAA>{Localization.Get("panel.not_specified")}</color>";
        AddWrappedText($"{Localization.Get("panel.editor")}: {editorsRich}", 18, Color.white);

        if (map.Catalog == MapCatalogKind.ArchitectServer)
        {
            AddServerMapInfo(map);
        }
        else if (map.Catalog == MapCatalogKind.EventCommunity)
        {
            AddEventMapInfo(map);
            AddMapIntegrationsSection(map);
        }
        else
        {
            var tagItems = (map.Tags ?? new List<MapTag>())
                .Select(t => (TagConfig.GetLabel(t), (Color32)TagConfig.GetColor(t)))
                .ToList();
            string tagsRich = tagItems.Count > 0
                ? BuildColoredList(tagItems)
                : $"<color=#AAAAAA>{Localization.Get("panel.none")}</color>";
            AddWrappedText($"{Localization.Get("panel.tags")}: {tagsRich}", 18, Color.white);

            AddText($"{Localization.Get("panel.rating")}: {StarsToString(map.Stars)}", 18, TextAnchor.MiddleLeft, Color.white, 26);

            if (map.Verified)
            {
                string verifierPart = string.IsNullOrWhiteSpace(map.VerifierName)
                    ? Localization.Get("panel.verified_by_unknown")
                    : map.VerifierName;
                string datePart = map.VerificationDate.HasValue
                    ? $" ({map.VerificationDate.Value:dd.MM.yyyy})" : "";
                AddWrappedText($"{Localization.Get("panel.verified")}: {verifierPart}{datePart}", 18, GoodGreen);
            }
            else
            {
                AddText(Localization.Get("panel.not_verified"), 18, TextAnchor.MiddleLeft, BadRed, 26);
            }
        }
        AddSpacer(10);

        AddEditorsSection(map);

        if (map.FileKind == MapFileKind.Save)
        {
            AddSaveMapSection(map);
            return;
        }

        bool filesAvailable = map.FileKind != MapFileKind.None;
        string filesValue = filesAvailable
            ? Localization.Get("badge.files_available") + FormatKnownArchiveSize(map)
            : Localization.Get("badge.files_missing");
        AddBadge(Localization.Get("badge.files"), filesValue,
            filesAvailable ? PositiveBadgeColor : NegativeBadgeColor);

        if (filesAvailable)
            _ = EnsureArchiveSizeAsync(map);

        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        string targetFolder = manager?.GetTargetFolder(map);
        bool isDownloaded = manager != null && manager.IsMapDownloaded(map);
        bool canLaunch = manager != null && manager.CanLaunchMap(map);
        bool isLaunched = manager != null && manager.IsMapLaunched(map);

        bool isDownloadingNow =
                                _reinstallingMapKeys.Contains(map.Name) ||
                                (manager != null && manager.IsMapDownloading(map));

        if (isDownloadingNow)
        {
            var (barGo, fillImage, barText) = UIFactory.CreateProgressBar(_content, "DownloadProgressBar");
            SetRowHeight(barGo, 44);
            _contentGos.Add(barGo);
            _downloadProgressFill = fillImage;
            _downloadProgressText = barText;
            var (received, total) = manager != null
                ? manager.GetMapDownloadProgress(map) : (0L, (long?)null);
            UpdateDownloadProgressBar(received, total);
            var (row_cancelGo, cancelGo, cancelButton, cancelImage, cancelText) = UIFactory.CreateCompactButtonRow(
                _content, "CancelDownloadButton", Localization.Get("button.cancel_download"), 16, 30f);
            cancelImage.color = ReinstallRedBg;
            cancelText.color = UIFactory.GetReadableTextColor(ReinstallRedBg);
            _contentGos.Add(row_cancelGo);
            cancelButton.onClick.AddListener(() => manager?.CancelMapDownload(map));
        }
        else
        {
            string downloadButtonLabel;
            if (!isDownloaded)
                downloadButtonLabel = Localization.Get("button.download");
            else if (!canLaunch)
                downloadButtonLabel = Localization.Get("map.status.downloaded");
            else if (isLaunched)
                downloadButtonLabel = Localization.Get("map.status.launched");
            else
                downloadButtonLabel = Localization.Get("button.launch");

            var (row_downloadGo, downloadGo, downloadButton, downloadImage, downloadText) = UIFactory.CreateCompactButtonRow(
                _content, "DownloadButton", downloadButtonLabel, 20, 40f);
            downloadImage.color = PrimaryActionBg;
            downloadText.color = UIFactory.GetReadableTextColor(PrimaryActionBg);
            _contentGos.Add(row_downloadGo);

            if (!isDownloaded && manager.TryGetLastDownloadError(map, out var lastError))
                AddWrappedText(Localization.Get("download.last_error", lastError), 15, BadRed);

            AddUnloadButton(map);
            _downloadButton = downloadButton;
            _downloadButtonText = downloadText;

            bool interactable;
            if (!isDownloaded) interactable = filesAvailable;
            else if (!canLaunch) interactable = false;
            else interactable = !isLaunched && !_launchInProgress;

            downloadButton.interactable = interactable;
            downloadButton.onClick.AddListener(() =>
            {
                if (!isDownloaded) OnDownloadClicked(map);
                else OnLaunchClicked(map);
            });

            if (isDownloaded && canLaunch && isLaunched)
            {
                downloadImage.color = LaunchedGreenBg;
                downloadText.color = UIFactory.GetReadableTextColor(LaunchedGreenBg);
            }
            else if (isDownloaded && canLaunch && !isLaunched)
            {
                downloadImage.color = DownloadedYellowBg;
                downloadText.color = UIFactory.GetReadableTextColor(DownloadedYellowBg);
            }
        }

        var (row_editorFolderGo, editorFolderGo, editorFolderButton, editorFolderImage, editorFolderText) = UIFactory.CreateCompactButtonRow(
                _content, "OpenEditorFolderButton", Localization.Get("button.open_editor_folder"), 18, 32f);
        editorFolderImage.color = DarkButtonBg;
        editorFolderText.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(row_editorFolderGo);
        editorFolderButton.onClick.AddListener(() => OnOpenEditorFolderClicked(map));

        if (isDownloaded)
        {
            var txtScan = TxtFileDetector.Scan(targetFolder);
            if (txtScan.FileNames.Count > 0)
            {
                string fileList = FormatTxtFileList(txtScan.FileNames);
                string message = txtScan.HasReadmeNamed
                    ? Localization.Get("txt.readme_found", fileList)
                    : Localization.Get("txt.file_found", fileList);
                AddWrappedText(message, 16, TxtNoticeYellow);
            }
        }
        AddSpacer(10);

        if (map.Catalog != MapCatalogKind.ArchitectServer)
        {
            bool hasRequiredMods = map.RequiredPublicMods != null && map.RequiredPublicMods.Count > 0;
            AddBadge(Localization.Get("badge.public_mods"),
                hasRequiredMods ? Localization.Get("badge.yes") : Localization.Get("badge.no"),
                hasRequiredMods ? PositiveBadgeColor : NeutralBadgeColor);

            if (hasRequiredMods)
            {
                AddModRows(map.RequiredPublicMods
                    .Select(n => (Display: n, Folder: ModFolderManager.ResolveFolderName(n), ModLinksName: n)));
                bool allPublicModsInstalled = map.RequiredPublicMods.All(RequiredModsChecker.IsModInstalled);
                if (!allPublicModsInstalled)
                {
                    var (row_installPublicGo, installPublicGo, installPublicButton, installPublicImage, installPublicText) = UIFactory.CreateCompactButtonRow(
                    _content, "InstallPublicModsButton", Localization.Get("button.install_public_mods"), 20, 36f);
                    installPublicImage.color = ModsActionBg;
                    installPublicText.color = UIFactory.GetReadableTextColor(ModsActionBg);
                    _contentGos.Add(row_installPublicGo);
                    _installPublicModsButton = installPublicButton;
                    _installPublicModsButtonText = installPublicText;
                    installPublicButton.interactable = !_installPublicModsInProgress;
                    installPublicButton.onClick.AddListener(() => OnInstallPublicModsClicked(map));
                }
            }
            AddSpacer(10);

            if (!isDownloaded)
            {
                AddBadge(Localization.Get("badge.additional_mods"),
                    Localization.Get("badge.additional_mods_after_download"), NeutralBadgeColor);
            }
            else
            {
                var dllNames = MapFileDistributor.ListDllFileNames(targetFolder);
                bool hasDlls = dllNames.Count > 0;
                AddBadge(Localization.Get("badge.additional_mods"),
                    hasDlls ? Localization.Get("badge.yes") : Localization.Get("badge.no"),
                    hasDlls ? PositiveBadgeColor : NeutralBadgeColor);
                if (hasDlls)
                {
                    AddModRows(dllNames
                        .Select(n => (Display: n, Folder: Path.GetFileNameWithoutExtension(n), ModLinksName: (string)null)));
                    var (row_installGo, installGo, installButton, installImage, installText) = UIFactory.CreateCompactButtonRow(
                    _content, "InstallModsButton", Localization.Get("button.install_additional_mods"), 20, 36f);
                    installImage.color = ModsActionBg;
                    installText.color = UIFactory.GetReadableTextColor(ModsActionBg);
                    _contentGos.Add(row_installGo);
                    _installModsButton = installButton;
                    _installModsButtonText = installText;
                    installButton.onClick.AddListener(() => OnInstallModsClicked(map));
                }
            }
        }

        var (row_modsFolderGo, modsFolderGo, modsFolderButton, modsFolderImage, modsFolderText) = UIFactory.CreateCompactButtonRow(
                _content, "OpenModsFolderButton", Localization.Get("button.open_mods_folder"), 18, 32f);
        modsFolderImage.color = DarkButtonBg;
        modsFolderText.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(row_modsFolderGo);
        modsFolderButton.onClick.AddListener(OnOpenModsFolderClicked);

        AddBackupsButton();

        AddRestartSection();

        if (isDownloaded)
        {
            AddSpacer(16);
            var (row_reinstallGo, reinstallGo, reinstallButton, reinstallImage, reinstallText) = UIFactory.CreateCompactButtonRow(
                _content, "ReinstallButton", Localization.Get("button.reinstall"), 18, 30f);
            reinstallImage.color = ReinstallRedBg;
            reinstallText.color = UIFactory.GetReadableTextColor(ReinstallRedBg);
            _contentGos.Add(row_reinstallGo);
            _reinstallButton = reinstallButton;
            _reinstallButtonText = reinstallText;
            reinstallButton.interactable = !_reinstallingMapKeys.Contains(map.Name);
            reinstallButton.onClick.AddListener(() => OnReinstallClicked(map));

            AddDeleteMapButton(map);
        }
    }

    private void OnOpenEditorFolderClicked(MapRow map)
    {
        if (map?.Catalog == MapCatalogKind.ArchitectServer && map.ServerSource == ArchitectSource.Silksong)
        {
            ShowNotification(Localization.Get("error.editor_not_in_game"), isError: true);
            return;
        }

        if (map?.Editors == null || map.Editors.Count == 0)
        {
            ShowNotification(Localization.Get("error.no_editor"), isError: true);
            return;
        }
        var foldersToOpen = new HashSet<string>();
        foreach (var editor in map.Editors)
        {
            switch (editor)
            {
                case MapEditor.DecorationMaster:
                    foldersToOpen.Add(GamePaths.DecorationMasterDataFolder);
                    break;
                case MapEditor.LegacyArchitect:
                case MapEditor.NewArchitect:
                    foldersToOpen.Add(GamePaths.ArchitectDataFolder);
                    break;
            }
        }
        if (foldersToOpen.Count == 0)
        {
            ShowNotification(Localization.Get("error.no_editor_folder"), isError: true);
            return;
        }
        try
        {
            foreach (var folder in foldersToOpen)
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                SystemUtils.OpenFolderInExplorer(folder);
            }
        }
        catch (Exception e)
        {
            ShowNotification(Localization.Get("error.open_editor_folder", e.Message), isError: true);
        }
    }

    private void OnOpenModsFolderClicked()
    {
        try
        {
            string modsFolder = Path.GetFullPath(GamePaths.ModsFolder);
            if (!Directory.Exists(modsFolder)) Directory.CreateDirectory(modsFolder);
            SystemUtils.OpenFolderInExplorer(modsFolder);
        }
        catch (Exception e)
        {
            ShowNotification(Localization.Get("error.open_mods_folder", e.Message), isError: true);
        }
    }

    private async void OnDownloadClicked(MapRow map)
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return;
        if (GlobalListAtlasMod.Instance?.Settings.IsOfflineMode == true)
        {
            ShowNotification(Localization.Get("error.offline_download"), isError: true);
            return;
        }
        var downloadTask = manager.DownloadMapFilesTrackedAsync(map);
        RefreshContent();
        var outcome = await downloadTask;

        if (!IsSameMap(_currentMap, map)) return;

        if (!outcome.Success) manager.ClearLastDownloadError(map);
        RefreshContent();
        if (!outcome.Success)
        {
            ShowNotification(Localization.Get("error.generic", outcome.ErrorMessage), isError: true);
            return;
        }
        if (outcome.MissingEditors.Count > 0)
        {
            string labels = string.Join(", ", outcome.MissingEditors.Select(EditorConfig.GetLabel));
            ShowNotification(Localization.Get("error.editor_not_installed", labels), isError: true);
        }
        if (outcome.TxtFileNames.Count > 0)
        {
            string fileList = FormatTxtFileList(outcome.TxtFileNames);
            string message = outcome.HasReadmeNamedTxt
                ? Localization.Get("txt.readme_downloaded", fileList)
                : Localization.Get("txt.file_downloaded", fileList);
            ShowNotification(message, isError: false);
        }
        ShowPresetDownloadResult(map);
    }

    private static bool IsSameMap(MapRow a, MapRow b) =>
        a != null && b != null && a.Catalog == b.Catalog &&
        string.Equals(a.Name, b.Name, StringComparison.Ordinal);

    private void OnLaunchClicked(MapRow map)
    {
        if (_launchInProgress) return;
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return;
        _launchInProgress = true;
        if (_downloadButton != null) _downloadButton.interactable = false;
        if (_downloadButtonText != null) _downloadButtonText.text = Localization.Get("button.launching");
        var outcome = manager.LaunchMap(map);
        _launchInProgress = false;
        if (!IsSameMap(_currentMap, map)) return;
        if (!outcome.Success)
        {
            if (_downloadButton != null) _downloadButton.interactable = true;
            if (_downloadButtonText != null) _downloadButtonText.text = Localization.Get("button.launch");
            ShowNotification(Localization.Get("error.generic", outcome.ErrorMessage), isError: true);
            return;
        }
        var activatedPresets = PresetManager.ActivatePresets(map);
        RefreshContent();

        foreach (var warning in GetEditorWarnings(map))
            ShowNotification(warning, isError: true);

        if (activatedPresets.Count > 0)
            ShowNotification(Localization.Get("presets.activated", string.Join(", ", activatedPresets)), isError: false);

        string mapFolder = manager.GetTargetFolder(map);
        RefreshEditorsAfterChange(map, map.Editors, mapFolder,
            SessionSceneTracker.GetDecorationMasterSceneNames(mapFolder));
    }

    private async void RefreshEditorsAfterChange(MapRow map, IEnumerable<MapEditor> editors, string mapFolder, List<string> sceneNames)
    {
        var set = new HashSet<MapEditor>(editors ?? Enumerable.Empty<MapEditor>());

        if (set.Contains(MapEditor.DecorationMaster) && !DecorationMasterRuntimeBridge.Refresh(sceneNames))
            Log.Warn("[Панель] Decoration Master не загружен или его устройство изменилось — кэш сцен не сброшен");

        if (!set.Contains(MapEditor.NewArchitect)) return;

        var result = await ArchitectRuntimeBridge.RefreshAsync(mapFolder);
        if (result.NotLoaded) return;
        if (map != null && !IsSameMap(_currentMap, map)) return;

        if (!result.Success)
        {
            ShowNotification(Localization.Get("launch.architect_refresh_failed", result.ErrorMessage), isError: true);
            return;
        }

        if (result.AssetsDownloaded > 0)
            ShowNotification(Localization.Get("launch.architect_assets_downloaded", result.AssetsDownloaded), isError: false);
        if (result.AssetsFailed > 0)
            ShowNotification(Localization.Get("launch.architect_assets_failed", result.AssetsFailed), isError: true);
    }

    private static List<string> GetEditorWarnings(MapRow map)
    {
        var warnings = new List<string>();
        var changedSinceStart = RestartTracker.GetChanges();

        foreach (var editor in (map.Editors ?? new List<MapEditor>()).Distinct())
        {
            var info = EditorModRegistry.Find(editor);
            if (info == null) continue;

            string label = EditorConfig.GetLabel(editor);
            var state = PendingModChanges.GetEffectiveState(info.FolderName);

            if (state == ModState.NotInstalled)
                warnings.Add(Localization.Get("launch.editor_missing", label));
            else if (state == ModState.Disabled)
                warnings.Add(Localization.Get("launch.editor_disabled", label));
            else if (changedSinceStart.Any(c => c.EnabledNow &&
                         string.Equals(c.ModFolder, info.FolderName, StringComparison.OrdinalIgnoreCase)))
                warnings.Add(Localization.Get("launch.editor_needs_restart", label));
        }

        return warnings;
    }

    private async void OnReinstallClicked(MapRow map)
    {
        var key = map.Name;
        if (_reinstallingMapKeys.Contains(key)) return;
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return;
        if (GlobalListAtlasMod.Instance?.Settings.IsOfflineMode == true)
        {
            ShowNotification(Localization.Get("error.offline_reinstall"), isError: true);
            return;
        }
        _reinstallingMapKeys.Add(key);
        if (_reinstallButton != null) _reinstallButton.interactable = false;
        if (_reinstallButtonText != null) _reinstallButtonText.text = Localization.Get("button.reinstalling");
        if (_downloadButton != null) _downloadButton.interactable = false;
        RefreshContent();
        bool sizeNotified = false;
        var outcome = await manager.ReinstallMapAsync(map, sizeBytes =>
        {
            if (!sizeNotified && sizeBytes > 5 * 1024 * 1024)
            {
                sizeNotified = true;
                float sizeMb = sizeBytes / (1024f * 1024f);
                ShowNotification(Localization.Get("error.large_file", sizeMb), isError: false);
            }
        });
        _reinstallingMapKeys.Remove(key);
        if (!IsSameMap(_currentMap, map)) return;
        if (!outcome.Success)
        {
            if (_reinstallButton != null) _reinstallButton.interactable = true;
            if (_reinstallButtonText != null) _reinstallButtonText.text = Localization.Get("button.reinstall");
            ShowNotification(Localization.Get("error.reinstall_failed", outcome.ErrorMessage), isError: true);
            return;
        }
        RefreshContent();
        if (outcome.MissingEditors.Count > 0)
        {
            string labels = string.Join(", ", outcome.MissingEditors.Select(EditorConfig.GetLabel));
            ShowNotification(Localization.Get("error.editor_not_installed", labels), isError: true);
        }
        if (outcome.TxtFileNames.Count > 0)
        {
            string fileList = FormatTxtFileList(outcome.TxtFileNames);
            string message = outcome.HasReadmeNamedTxt
                ? Localization.Get("txt.readme_reinstalled", fileList)
                : Localization.Get("txt.file_reinstalled", fileList);
            ShowNotification(message, isError: false);
        }
        ShowPresetDownloadResult(map);
    }

    private void AddEditorsSection(MapRow map)
    {
        var editors = (map.Editors ?? new List<MapEditor>())
            .Where(EditorModRegistry.IsManaged)
            .Distinct()
            .ToList();

        if (editors.Count == 0) return;

        AddSpacer(10);
        AddBadge(Localization.Get("editors.section"), "", NeutralBadgeColor);

        foreach (var editor in editors)
            AddEditorRow(editor);

        if (EditorModRegistry.ArchitectsConflict())
            AddWrappedText(Localization.Get("editors.architect_conflict"), 15, BadRed);

        if (!PublicModInstaller.ManifestsLoaded)
            _ = EnsureManifestsAsync();
    }

    private async Task EnsureManifestsAsync()
    {
        if (_manifestsRequested) return;
        _manifestsRequested = true;

        try
        {
            await PublicModInstaller.GetManifestsAsync();
            if (_currentMap != null) RefreshContent();
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось получить версии редакторов из ModLinks: {e.Message}");
        }
    }

    private void AddEditorRow(MapEditor editor)
    {
        var info0 = EditorModRegistry.Find(editor);
        var state = PendingModChanges.GetEffectiveState(info0?.FolderName);
        bool pending = PendingModChanges.IsPending(info0?.FolderName);
        string label = EditorConfig.GetLabel(editor);
        var color = (Color32)EditorConfig.GetColor(editor);

        string stateText;
        Color stateColor;
        string buttonKey;
        switch (state)
        {
            case ModState.Enabled:
                stateText = Localization.Get("editors.state.enabled");
                stateColor = GoodGreen;
                buttonKey = "editors.button.disable";
                break;
            case ModState.Disabled:
                stateText = Localization.Get("editors.state.disabled");
                stateColor = TxtNoticeYellow;
                buttonKey = "editors.button.enable";
                break;
            default:
                stateText = Localization.Get("editors.state.not_installed");
                stateColor = BadRed;
                buttonKey = "editors.button.install";
                break;
        }

        string version = EditorModRegistry.GetInstalledVersion(editor);
        if (string.IsNullOrWhiteSpace(version))
        {
            var info = EditorModRegistry.Find(editor);
            string available = PublicModInstaller.GetCachedManifest(info?.ModLinksName)?.Version;
            version = string.IsNullOrWhiteSpace(available)
                ? null
                : Localization.Get("editors.version.available", available);
        }
        else
        {
            version = "v" + version;
        }

        string versionPart = version == null ? "" : $"  <color=#AAAAAA>{version}</color>";
        if (pending) versionPart += $"  <color=#AAAAAA>({Localization.Get("mods.after_restart")})</color>";
        AddWrappedText($"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{label}</color>  —  " +
                       $"<color=#{ColorUtility.ToHtmlStringRGB(stateColor)}>{stateText}</color>{versionPart}",
                       16, Color.white);

        var (rowGo, btnGo, button, image, text) = UIFactory.CreateCompactButtonRow(
            _content, $"EditorButton_{editor}", Localization.Get(buttonKey), 15, 28f, 12f, 80f);
        image.color = DarkButtonBg;
        text.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(rowGo);

        var capturedState = state;
        button.interactable = !_editorActionInProgress;
        button.onClick.AddListener(() =>
        {
            if (capturedState == ModState.NotInstalled)
                OnEditorInstallClicked(editor, text);
            else
                OnEditorToggleClicked(editor, capturedState == ModState.Disabled);
        });
    }

    private async void OnEditorToggleClicked(MapEditor editor, bool enable)
    {
        var info = EditorModRegistry.Find(editor);
        if (enable && info != null)
            await PublicModInstaller.EnableDependenciesAsync(info.ModLinksName);
        string error = info == null
            ? $"Редактор {editor} не поддерживает включение/выключение"
            : ModFolderManager.SetEnabledOrDefer(info.FolderName, enable, out _);

        if (error == null && !enable) PresetManager.DeactivateForEditors(new[] { editor });
        RefreshContent();

        if (error != null)
        {
            ShowNotification(Localization.Get("editors.action_failed", error), isError: true);
            return;
        }

        string label = EditorConfig.GetLabel(editor);
        ShowRestartStateNotification(
            Localization.Get(enable ? "editors.enabled_restart" : "editors.disabled_restart", label));

        if (EditorModRegistry.ArchitectsConflict())
            ShowNotification(Localization.Get("editors.architect_conflict"), isError: true);
    }

    private async void OnEditorInstallClicked(MapEditor editor, Text buttonText)
    {
        if (_editorActionInProgress) return;

        var info = EditorModRegistry.Find(editor);
        if (info == null) return;

        _editorActionInProgress = true;
        if (buttonText != null) buttonText.text = Localization.Get("editors.installing");

        var result = await PublicModInstaller.DownloadPublicModAsync(info.ModLinksName);

        _editorActionInProgress = false;
        RefreshContent();

        string label = EditorConfig.GetLabel(editor);
        if (result.Success)
            ShowRestartStateNotification(Localization.Get("editors.installed_restart", label));
        else
            ShowNotification(Localization.Get("editors.install_failed", label, result.ErrorMessage), isError: true);
    }

    private void AddModRow(string displayName, string modFolderName, string modLinksName)
    {
        var state = PendingModChanges.GetEffectiveState(modFolderName);
        bool pending = PendingModChanges.IsPending(modFolderName);

        string stateText;
        Color stateColor;
        switch (state)
        {
            case ModState.Enabled:
                stateText = Localization.Get("editors.state.enabled");
                stateColor = GoodGreen;
                break;
            case ModState.Disabled:
                stateText = Localization.Get("editors.state.disabled");
                stateColor = TxtNoticeYellow;
                break;
            default:
                stateText = Localization.Get("editors.state.not_installed");
                stateColor = BadRed;
                break;
        }

        string pendingMark = pending ? $"  <color=#AAAAAA>({Localization.Get("mods.after_restart")})</color>" : "";
        AddWrappedText($"  • {displayName}  —  <color=#{ColorUtility.ToHtmlStringRGB(stateColor)}>{stateText}</color>{pendingMark}",
                       15, Color.white);

        if (state == ModState.NotInstalled && string.IsNullOrEmpty(modLinksName))
            return;

        string buttonKey = state switch
        {
            ModState.Enabled => "editors.button.disable",
            ModState.Disabled => "editors.button.enable",
            _ => "editors.button.install"
        };

        var (rowGo, btnGo, button, image, text) = UIFactory.CreateCompactButtonRow(
            _content, $"ModButton_{modFolderName}", Localization.Get(buttonKey), 14, 26f, 24f, 80f);
        image.color = DarkButtonBg;
        text.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(rowGo);

        var capturedState = state;
        button.interactable = !_editorActionInProgress;
        button.onClick.AddListener(() =>
        {
            if (capturedState == ModState.NotInstalled)
                OnSingleModInstallClicked(modLinksName, text);
            else
                OnModToggleClicked(displayName, modFolderName, capturedState == ModState.Disabled);
        });

        AddMissingDependenciesRow(modLinksName, state);
    }

    private void AddMissingDependenciesRow(string modLinksName, ModState state)
    {
        if (string.IsNullOrEmpty(modLinksName) || state == ModState.NotInstalled) return;

        var missing = PublicModInstaller.GetMissingDependencies(modLinksName);
        if (missing.Count == 0) return;

        AddWrappedText(Localization.Get("mods.missing_dependencies", string.Join(", ", missing)), 14, BadRed);

        var (rowGo, btnGo, button, image, text) = UIFactory.CreateCompactButtonRow(
            _content, $"DepsButton_{modLinksName}", Localization.Get("button.install_dependencies"), 14, 26f, 24f, 80f);
        image.color = ModsActionBg;
        text.color = UIFactory.GetReadableTextColor(ModsActionBg);
        button.interactable = !_editorActionInProgress;
        _contentGos.Add(rowGo);
        button.onClick.AddListener(() => OnSingleModInstallClicked(modLinksName, text));
    }

    private void AddModRows(IEnumerable<(string Display, string Folder, string ModLinksName)> mods)
    {
        var list = mods.ToList();
        if (list.Count == 0) return;

        AddText(Localization.Get("mods.needed"), 15, TextAnchor.MiddleLeft, MutedGray, 20);
        foreach (var m in list)
            AddModRow(m.Display, m.Folder, m.ModLinksName);

        int installed = list.Count(m => PendingModChanges.GetEffectiveState(m.Folder) != ModState.NotInstalled);
        if (installed == list.Count)
            AddText(Localization.Get("mods.all_installed"), 16, TextAnchor.MiddleLeft, GoodGreen, 26);
        else
            AddText(Localization.Get("mods.installed", installed, list.Count), 15, TextAnchor.MiddleLeft, MutedGray, 20);
    }

    private void OnModToggleClicked(string displayName, string modFolderName, bool enable)
    {
        string error = ModFolderManager.SetEnabledOrDefer(modFolderName, enable, out bool deferred);
        RefreshContent();

        if (error != null)
        {
            ShowNotification(Localization.Get("editors.action_failed", error), isError: true);
            return;
        }

        ShowRestartStateNotification(
            Localization.Get(enable ? "editors.enabled_restart" : "editors.disabled_restart", displayName));
    }

    private async void OnSingleModInstallClicked(string modLinksName, Text buttonText)
    {
        if (_editorActionInProgress || string.IsNullOrEmpty(modLinksName)) return;

        _editorActionInProgress = true;
        if (buttonText != null) buttonText.text = Localization.Get("editors.installing");

        bool alreadyInstalled = RequiredModsChecker.IsModInstalled(modLinksName);
        var result = alreadyInstalled
            ? await PublicModInstaller.InstallMissingDependenciesAsync(modLinksName)
            : await PublicModInstaller.DownloadPublicModAsync(modLinksName);

        _editorActionInProgress = false;
        RefreshContent();

        if (!result.Success)
        {
            ShowNotification(Localization.Get("editors.install_failed", modLinksName, result.ErrorMessage), isError: true);
            return;
        }

        ShowRestartStateNotification(alreadyInstalled
            ? Localization.Get("mods.dependencies_installed", string.Join(", ", result.InstalledDependencies))
            : Localization.Get("editors.installed_restart", modLinksName));

        if (!alreadyInstalled && result.InstalledDependencies.Count > 0)
            ShowNotification(Localization.Get("mods.dependencies_installed", string.Join(", ", result.InstalledDependencies)), isError: false);

        if (result.FailedDependencies.Count > 0)
            ShowNotification(Localization.Get("mods.dependencies_failed", string.Join(", ", result.FailedDependencies)), isError: true);
    }

    private void ShowRestartStateNotification(string changeMessage)
    {
        if (IsRestartNeeded())
            ShowNotification(changeMessage, isError: false);
        else
            ShowNotification(Localization.Get("restart.not_needed"), isError: false);
    }

    private static bool IsRestartNeeded() =>
        RestartTracker.IsRestartNeeded() || SessionSceneTracker.RestartRequested;

    private void AddRestartSection()
    {
        if (!IsRestartNeeded()) return;

        AddSpacer(14);
        var reasons = new List<string>();
        string modChanges = RestartTracker.DescribeChanges();
        if (!string.IsNullOrEmpty(modChanges)) reasons.Add(modChanges);
        if (SessionSceneTracker.RestartRequested) reasons.Add(SessionSceneTracker.DescribeRestartReasons());
        AddWrappedText(Localization.Get("restart.needed", string.Join("; ", reasons)), 15, TxtNoticeYellow);

        string idle = Localization.Get("restart.button_idle");
        string holding = Localization.Get("restart.button_holding");

        var (row_go, go, button, image, text) = UIFactory.CreateCompactButtonRow(
                _content, "RestartGameButton", idle, 18, 36f);
        image.color = RestartButtonBg;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = UIFactory.GetReadableTextColor(RestartButtonBg);

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(go.transform, false);
        fillGo.transform.SetAsFirstSibling();
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        var fillImage = fillGo.GetComponent<Image>();
        fillImage.color = RestartButtonFill;
        fillImage.raycastTarget = false;

        UIFactory.FitButtonWidthForLabels(go, text, 120f, idle, holding);

        var hold = go.AddComponent<HoldToConfirmButton>();
        hold.Fill = fillImage;
        hold.Label = text;
        hold.IdleLabel = idle;
        hold.HoldingLabel = holding;
        hold.OnConfirmed = () =>
        {
            string error = GameRestarter.Restart();
            if (error != null)
                ShowNotification(Localization.Get("restart.failed", error), isError: true);
        };

        _contentGos.Add(row_go);
    }

    private void UpdateHeaderButtons()
    {
        bool hasMap = _currentMap != null;

        if (_favoriteButton != null) _favoriteButton.interactable = hasMap;
        bool architect = hasMap && _currentMap.Catalog == MapCatalogKind.ArchitectServer;
        if (_openSheetGo != null) _openSheetGo.SetActive(!architect);
        if (_openSheetButton != null) _openSheetButton.interactable = hasMap && !architect;
        if (_favoriteText == null || _favoriteImage == null) return;

        bool favorite = hasMap && FavoritesStore.IsFavorite(_currentMap);
        _favoriteText.text = Localization.Get(favorite ? "favorite.remove" : "favorite.add");
        _favoriteImage.color = favorite ? FavoriteBg : DarkButtonBg;
        _favoriteText.color = UIFactory.GetReadableTextColor(favorite ? FavoriteBg : DarkButtonBg);
    }

    private void OnFavoriteClicked()
    {
        if (_currentMap == null) return;

        FavoritesStore.Toggle(_currentMap);
        UpdateHeaderButtons();
    }

    private void OnOpenSheetClicked()
    {
        if (_currentMap == null) return;

        SystemUtils.OpenUrl(_currentMap.Catalog == MapCatalogKind.EventCommunity
            ? EventCatalogConfig.GetRowUrl(_currentMap.SheetRowNumber)
            : SheetConfig.GetRowUrl(_currentMap.SheetRowNumber));
    }

    private string FormatKnownArchiveSize(MapRow map)
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager != null && manager.TryGetArchiveSize(map, out long bytes))
        {
            double mb = bytes / (1024.0 * 1024.0);
            string size = mb >= 1 ? $"{mb:F1} МБ" : $"{bytes / 1024.0:F0} КБ";
            return $"  ({size})";
        }

        return "";
    }

    private async Task EnsureArchiveSizeAsync(MapRow map)
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null || manager.TryGetArchiveSize(map, out _)) return;
        if (!_sizeRequested.Add(map.Name)) return;

        bool ok = await manager.FetchArchiveSizeAsync(map);
        if (ok && IsSameMap(_currentMap, map))
            RefreshContent();
    }

    private void AddUnloadButton(MapRow map)
    {
        var editors = map?.Editors;
        if (editors == null || editors.Count == 0) return;
        if (!MapFileDistributor.HasActiveFiles(editors)) return;

        var (row_unloadGo, unloadGo, unloadButton, unloadImage, unloadText) = UIFactory.CreateCompactButtonRow(
            _content, "UnloadMapButton", Localization.Get("button.unload_map"), 16, 30f);
        unloadImage.color = ReinstallRedBg;
        unloadText.color = UIFactory.GetReadableTextColor(ReinstallRedBg);
        _contentGos.Add(row_unloadGo);

        unloadButton.onClick.AddListener(() => OnUnloadClicked(map));
    }

    private void OnUnloadClicked(MapRow map)
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return;

        string error = manager.UnloadMap(map);

        if (error == null || error == "NOTHING")
        {
            PresetManager.DeactivatePresets(map);
            PresetManager.DeactivateForEditors(map.Editors);
        }
        RefreshContent();

        if (error == null)
        {
            ShowNotification(Localization.Get("unload.done"), isError: false);
            RefreshEditorsAfterChange(map, map.Editors, null, null);
        }
        else if (error == "NOTHING")
            ShowNotification(Localization.Get("unload.nothing"), isError: false);
        else
            ShowNotification(Localization.Get("unload.failed", error), isError: true);
    }

    private static readonly System.Net.Http.HttpClient PreviewHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private void AddServerMapInfo(MapRow map)
    {
        if (!string.IsNullOrWhiteSpace(map.Description))
        {
            AddSpacer(4);
            AddWrappedText(map.Description, 16, new Color(0.86f, 0.87f, 0.90f));
        }

        AddSpacer(6);
        if (map.HasServerMetadata)
        {
            AddWrappedText($"{Localization.Get("server.difficulty")}: {DifficultyText(map.Difficulty)}   ·   " +
                           $"{Localization.Get("server.duration")}: {DurationText(map.Duration)}", 16, Color.white);

            if (map.ServerTags.Count > 0)
                AddWrappedText($"{Localization.Get("panel.tags")}: " +
                               string.Join(", ", map.ServerTags.Select(t => Tinted(t.ToString(), ArchitectServerConfig.GetTagColor(t)))),
                               16, Color.white);

            AddServerStatsRow(map);
        }
        else
        {
            AddServerStatsRow(map);
        }

        if (map.Uploaded.HasValue)
        {
            string dates = $"{Localization.Get("server.uploaded")}: {map.Uploaded.Value.ToLocalTime():dd.MM.yyyy}";
            if (map.Updated.HasValue && map.Updated.Value.Date != map.Uploaded.Value.Date)
                dates += $"   ·   {Localization.Get("server.updated")}: {map.Updated.Value.ToLocalTime():dd.MM.yyyy}";
            AddWrappedText(dates, 15, MutedGray);
        }

        if (map.ServerSource == ArchitectSource.Silksong)
        {
            AddSpacer(4);
            AddWrappedText(Localization.Get("server.silksong_download_only"), 15, TxtNoticeYellow);
        }

        AddMarksRow(map);
        AddServerSaveButton(map);
    }

    private void AddServerStatsRow(MapRow map)
    {
        string downloads = $"{Localization.Get("server.downloads")}: {map.Downloads}";
        string stats = map.HasServerMetadata
            ? $"{downloads}   ·   {Localization.Get("server.likes")}: {map.Likes}"
            : downloads;
        AddWrappedText(stats, 16, Color.white);
    }

    private static string DifficultyText(ServerDifficulty d) =>
        d == ServerDifficulty.None
            ? Localization.Get("server.not_set")
            : Tinted(ArchitectServerConfig.GetDifficultyLabel(d), ArchitectServerConfig.GetDifficultyColor(d));

    private static string DurationText(ServerDuration d) =>
        d == ServerDuration.None
            ? Localization.Get("server.not_set")
            : Tinted(ArchitectServerConfig.GetDurationLabel(d), ArchitectServerConfig.GetDurationColor(d));

    private static string Tinted(string text, Color32 color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

    private void AddMarksRow(MapRow map)
    {
        AddSpacer(6);
        var row = new GameObject("MarksRow", typeof(RectTransform));
        row.transform.SetParent(_content, false);
        SetRowHeight(row, 30);
        _contentGos.Add(row);

        string seenOn = "● " + Localization.Get("marks.seen");
        string seenOff = "● " + Localization.Get("marks.not_seen");
        var seenColor = new Color(0.22f, 0.40f, 0.52f, 1f);
        var likedColor = new Color(0.22f, 0.45f, 0.27f, 1f);
        var dislikedColor = new Color(0.50f, 0.22f, 0.24f, 1f);

        float seenWidth;
        {
            var (measureGo, _, _, measureText) = UIFactory.CreateButton(row.transform, "MarkMeasure", seenOn, 15);
            float onWidth = UIFactory.MeasureButtonWidth(measureText, min: 60f);
            measureText.text = seenOff;
            seenWidth = Mathf.Max(onWidth, UIFactory.MeasureButtonWidth(measureText, min: 60f));
            Destroy(measureGo);
        }

        float x = 0f;
        (Button button, Image image, Text text) Place(string name, string label, float width)
        {
            var (go, button, image, text) = UIFactory.CreateButton(row.transform, name, label, 15);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.AddOutline(go);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 0.5f);
            if (width <= 0f) width = UIFactory.MeasureButtonWidth(text, min: 60f);
            rect.sizeDelta = new Vector2(width, 0);
            rect.anchoredPosition = new Vector2(x, 0);
            x += width + 6f;
            return (button, image, text);
        }

        var (seenButton, seenImage, seenText) = Place("Mark_Seen", seenOn, seenWidth);
        var (likedButton, likedImage, likedText) = Place("Mark_Liked", "▲", 0f);
        var (dislikedButton, dislikedImage, dislikedText) = Place("Mark_Disliked", "▼", 0f);

        void Paint(Image image, Text text, bool on, Color onColor)
        {
            image.color = on ? onColor : DarkButtonBg;
            text.color = UIFactory.GetReadableTextColor(image.color);
        }

        void Refresh()
        {
            bool seen = MapMarksStore.IsSeen(map);
            var reaction = MapMarksStore.GetReaction(map);
            seenText.text = seen ? seenOn : seenOff;
            Paint(seenImage, seenText, seen, seenColor);
            Paint(likedImage, likedText, reaction == MapReaction.Liked, likedColor);
            Paint(dislikedImage, dislikedText, reaction == MapReaction.Disliked, dislikedColor);
        }

        seenButton.onClick.AddListener(() => { MapMarksStore.SetSeen(map, !MapMarksStore.IsSeen(map)); Refresh(); });
        likedButton.onClick.AddListener(() => { MapMarksStore.ToggleReaction(map, MapReaction.Liked); Refresh(); });
        dislikedButton.onClick.AddListener(() => { MapMarksStore.ToggleReaction(map, MapReaction.Disliked); Refresh(); });
        Refresh();
    }

    private bool _serverSaveInProgress;

    private void AddServerSaveButton(MapRow map)
    {
        if (!map.HasSave || map.ServerSource == ArchitectSource.Silksong) return;

        AddSpacer(6);
        var (row, go, button, image, text) = UIFactory.CreateCompactButtonRow(
            _content, "ServerSaveButton",
            Localization.Get(_serverSaveInProgress ? "save.installing" : "server.install_save"), 16, 32f);
        image.color = ModsActionBg;
        text.color = UIFactory.GetReadableTextColor(ModsActionBg);
        button.interactable = !_serverSaveInProgress;
        _contentGos.Add(row);
        button.onClick.AddListener(() => OnServerSaveClicked(map));
    }

    private async void OnServerSaveClicked(MapRow map)
    {
        if (_serverSaveInProgress) return;
        _serverSaveInProgress = true;
        RefreshContent();

        SaveInstallResult result;
        try
        {
            var bytes = await Server.ArchitectServerClient.DownloadSaveAsync(map);
            result = SaveInstaller.InstallBytes(map.Name, bytes);
        }
        catch (Exception e)
        {
            result = new SaveInstallResult { ErrorMessage = e.Message };
        }

        _serverSaveInProgress = false;
        if (!IsSameMap(_currentMap, map)) return;
        RefreshContent();

        if (!result.Success)
        {
            ShowNotification(Localization.Get("save.failed", result.ErrorMessage), isError: true);
            return;
        }

        ShowNotification(Localization.Get("save.installed", result.Slot, result.SavePath), isError: false);
        if (result.NeedsMoreSaves && !SaveInstaller.IsMoreSavesInstalled())
            ShowNotification(Localization.Get("save.slot_needs_more_saves", result.Slot), isError: true);
    }

    private const string TeleportMasterModName = "Teleport Master";

    private static readonly Dictionary<string, Texture2D> PreviewCache = new();
    private static CancellationTokenSource _previewCts;
    private static string _previewCtsUrl;
    private GameObject _previewPlaceholder;
    private string _previewPlaceholderUrl;
    private bool _saveInstallInProgress;
    private ScrollRect _detailsScroll;

    private void AddEventMapInfo(MapRow map)
    {
        string typeColor = ColorUtility.ToHtmlStringRGB(EventCatalogConfig.GetTypeColor(map.EventType));
        AddWrappedText($"{Localization.Get("panel.type")}: <color=#{typeColor}>{EventCatalogConfig.GetTypeLabel(map.EventType)}</color>",
            18, Color.white);

        if (!string.IsNullOrWhiteSpace(map.Description))
        {
            AddSpacer(4);
            AddWrappedText(map.Description, 16, new Color(0.86f, 0.87f, 0.90f));
        }

        if (!string.IsNullOrEmpty(map.RulesUrl))
        {
            AddSpacer(4);
            var (rulesRow, rulesGo, rulesButton, rulesImage, rulesText) = UIFactory.CreateCompactButtonRow(
                _content, "RulesButton", Localization.Get("button.open_rules"), 16, 32f);
            rulesImage.color = DarkButtonBg;
            rulesText.color = UIFactory.GetReadableTextColor(DarkButtonBg);
            _contentGos.Add(rulesRow);
            string url = map.RulesUrl;
            rulesButton.onClick.AddListener(() => SystemUtils.OpenUrl(url));
        }

        AddPreviewImage(map);
    }

    private void AddPreviewImage(MapRow map)
    {
        if (string.IsNullOrEmpty(map.PreviewUrl)) return;

        AddSpacer(8);

        if (!PreviewCache.TryGetValue(map.PreviewUrl, out var texture) || texture == null)
        {
            var placeholder = AddText(Localization.Get("panel.preview_loading"), 15, TextAnchor.MiddleLeft, MutedGray, 24);
            _previewPlaceholder = placeholder.gameObject;
            _previewPlaceholderUrl = map.PreviewUrl;
            _ = LoadPreviewAsync(map);
            return;
        }

        _contentGos.Add(CreatePreviewGo(texture));
    }

    private GameObject CreatePreviewGo(Texture2D texture)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        var layout = _content.GetComponent<VerticalLayoutGroup>();
        float padding = layout != null ? layout.padding.left + layout.padding.right : 0f;
        float width = Mathf.Max(_content.rect.width - padding, 1f);
        float height = width * texture.height / Mathf.Max(1f, texture.width);

        var go = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(_content, false);
        go.GetComponent<RawImage>().texture = texture;
        SetRowHeight(go, height);
        return go;
    }

    private void ShowPreview(string url, Texture2D texture)
    {
        if (_avatarUrl == url && _avatarImage != null)
        {
            if (_avatarGo != null) _avatarGo.SetActive(true);
            ShowAvatarTexture(texture);
            if (_avatarProgressGo != null) _avatarProgressGo.SetActive(false);
            return;
        }

        if (_previewPlaceholder == null || _previewPlaceholderUrl != url) return;

        var placeholder = _previewPlaceholder;
        _previewPlaceholder = null;
        int index = placeholder.transform.GetSiblingIndex();
        var go = CreatePreviewGo(texture);
        go.transform.SetSiblingIndex(index);

        int listIndex = _contentGos.IndexOf(placeholder);
        if (listIndex >= 0) _contentGos[listIndex] = go;
        else _contentGos.Add(go);
        Destroy(placeholder);
    }

    private async Task LoadPreviewAsync(MapRow map)
    {
        string url = map.PreviewUrl;
        if (FailedPreviewUrls.Contains(url))
        {
            HideAvatar(url);
            return;
        }
        if (_previewCtsUrl == url) return;

        _previewCts?.Cancel();
        var cts = new CancellationTokenSource(PreviewTimeout);
        _previewCts = cts;
        _previewCtsUrl = url;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            byte[] bytes = url.IndexOf("drive.google.com", StringComparison.OrdinalIgnoreCase) >= 0
                ? await Drive.GoogleDriveDownloader.DownloadAsync(url,
                    onProgress: (received, total) => ReportAvatarProgress(url, received, total),
                    cancellationToken: cts.Token)
                : await ReadPreviewBytesAsync(url, cts.Token,
                    (received, total) => ReportAvatarProgress(url, received, total));
            cts.Token.ThrowIfCancellationRequested();
            if (bytes == null || bytes.Length == 0)
            {
                FailedPreviewUrls.Add(url);
                Log.Warn($"[Превью] '{map.Name}': пустой ответ за {clock.ElapsedMilliseconds} мс, {url}");
                HideAvatar(url);
                return;
            }

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(bytes))
            {
                Log.Warn($"Превью карты '{map.Name}' не удалось прочитать как картинку");
                FailedPreviewUrls.Add(url);
                HideAvatar(url);
                return;
            }

            PreviewCache[url] = texture;
            if (IsSameMap(_currentMap, map)) ShowPreview(url, texture);
        }
        catch (OperationCanceledException)
        {
            if (_previewCts == cts)
            {
                Log.Warn($"[Превью] '{map.Name}': не загрузилась за {PreviewTimeout.TotalSeconds:F0} с, {url}");
                HideAvatar(url);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[Превью] '{map.Name}': ошибка за {clock.ElapsedMilliseconds} мс, {url}: {e.Message}");
            HideAvatar(url);
        }
        finally
        {
            if (_previewCts == cts)
            {
                _previewCts = null;
                _previewCtsUrl = null;
            }
        }
    }

    private static async Task<byte[]> ReadPreviewBytesAsync(string url, CancellationToken token, Action<long, long?> onProgress)
    {
        using var response = await PreviewHttp.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            FailedPreviewUrls.Add(url);
            response.EnsureSuccessStatusCode();
        }
        long? total = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[16384];
        long received = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
        {
            await memory.WriteAsync(buffer, 0, read, token);
            received += read;
            onProgress?.Invoke(received, total);
        }
        return memory.ToArray();
    }

    private const float AvatarHeight = 108f * 0.9f;
    private const float AvatarMaxWidth = AvatarHeight * 1.8f;
    private const float AvatarMinWidth = AvatarHeight * 0.75f;
    private const float AvatarTopOffset = 6f;
    private const float AvatarProgressHeight = 18f;
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(20);
    private static readonly HashSet<string> FailedPreviewUrls = new();
    private RawImage _avatarImage;
    private GameObject _avatarGo;
    private RectTransform _avatarRect;
    private RectTransform _avatarTitleRect;
    private GameObject _avatarProgressGo;
    private RectTransform _avatarProgressRect;
    private Image _avatarProgressFill;
    private Text _avatarProgressText;
    private string _avatarUrl;

    private Text AddArchitectTitleRow(MapRow map)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        var layout = _content.GetComponent<VerticalLayoutGroup>();
        float padding = layout != null ? layout.padding.left + layout.padding.right : 0f;
        bool hasAvatar = !string.IsNullOrEmpty(map.PreviewUrl);
        float reserved = hasAvatar ? AvatarMaxWidth + 10f : 0f;
        float textWidth = Mathf.Max(_content.rect.width - padding - reserved, 1f);

        var row = new GameObject("TitleRow", typeof(RectTransform));
        row.transform.SetParent(_content, false);
        _contentGos.Add(row);

        var title = UIFactory.CreateText(row.transform, "Title", map.Name, 26, TextAnchor.UpperLeft);
        title.color = map.CellColor;
        title.fontStyle = FontStyle.Bold;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        var titleRect = (RectTransform)title.transform;
        var settings = title.GetGenerationSettings(new Vector2(textWidth, 0f));
        settings.generateOutOfBounds = true;
        float titleHeight = new TextGenerator().GetPreferredHeight(map.Name, settings);
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0, 1);
        titleRect.offsetMin = new Vector2(0, -titleHeight);
        titleRect.offsetMax = new Vector2(-reserved, 0);

        var avatarGo = new GameObject("Avatar", typeof(RectTransform), typeof(RawImage));
        avatarGo.transform.SetParent(row.transform, false);
        var avatar = avatarGo.GetComponent<RawImage>();
        avatar.color = DarkButtonBg;
        var avatarRect = (RectTransform)avatarGo.transform;
        avatarRect.anchorMin = new Vector2(1, 1);
        avatarRect.anchorMax = new Vector2(1, 1);
        avatarRect.pivot = new Vector2(1, 1);
        avatarRect.sizeDelta = new Vector2(AvatarHeight, AvatarHeight);
        avatarRect.anchoredPosition = new Vector2(0, -AvatarTopOffset);

        var (progressGo, fill, progressText) = UIFactory.CreateProgressBar(row.transform, "AvatarProgress");
        var progressRect = (RectTransform)progressGo.transform;
        progressRect.anchorMin = new Vector2(1, 1);
        progressRect.anchorMax = new Vector2(1, 1);
        progressRect.pivot = new Vector2(1, 1);
        progressRect.sizeDelta = new Vector2(AvatarHeight, AvatarProgressHeight);
        progressRect.anchoredPosition = new Vector2(0, -(AvatarTopOffset + AvatarHeight - AvatarProgressHeight));
        progressText.fontSize = 10;
        progressText.text = "";

        SetRowHeight(row, titleHeight + 4f);

        _avatarGo = avatarGo;
        _avatarRect = avatarRect;
        _avatarTitleRect = titleRect;
        _avatarImage = avatar;
        _avatarProgressGo = progressGo;
        _avatarProgressRect = progressRect;
        _avatarProgressFill = fill;
        _avatarProgressText = progressText;
        _avatarUrl = map.PreviewUrl;

        if (!hasAvatar || FailedPreviewUrls.Contains(map.PreviewUrl))
        {
            avatarGo.SetActive(false);
            progressGo.SetActive(false);
        }
        else if (PreviewCache.TryGetValue(map.PreviewUrl, out var cached) && cached != null)
        {
            ShowAvatarTexture(cached);
            progressGo.SetActive(false);
        }
        else
        {
            _ = LoadPreviewAsync(map);
        }

        return title;
    }

    private void HideAvatar(string url)
    {
        if (_avatarUrl != url || _avatarGo == null) return;
        _avatarGo.SetActive(false);
        if (_avatarProgressGo != null) _avatarProgressGo.SetActive(false);
    }

    private void ShowAvatarTexture(Texture2D texture)
    {
        if (_avatarImage == null || _avatarRect == null) return;

        float aspect = (float)texture.width / Mathf.Max(1, texture.height);
        float width = Mathf.Clamp(AvatarHeight * aspect, AvatarMinWidth, AvatarMaxWidth);

        _avatarImage.texture = texture;
        _avatarImage.color = Color.white;
        _avatarImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        _avatarRect.sizeDelta = new Vector2(width, AvatarHeight);
        if (_avatarProgressRect != null) _avatarProgressRect.sizeDelta = new Vector2(width, AvatarProgressHeight);
        if (_avatarTitleRect != null) _avatarTitleRect.offsetMax = new Vector2(-(width + 10f), 0);
        _avatarGo.SetActive(true);
    }

    private void ReportAvatarProgress(string url, long received, long? total)
    {
        if (_avatarUrl != url || _avatarProgressGo == null) return;

        _avatarProgressGo.SetActive(true);
        bool known = total.HasValue && total.Value > 0;
        float fraction = known ? Mathf.Clamp01((float)received / total.Value) : 0.5f;
        if (_avatarProgressFill != null)
            ((RectTransform)_avatarProgressFill.transform).anchorMax = new Vector2(fraction, 1);
        if (_avatarProgressText != null)
            _avatarProgressText.text = known ? $"{fraction * 100f:F0}%" : FormatBytes(received);
    }

    private void AddSaveMapSection(MapRow map)
    {
        AddBadge(Localization.Get("badge.save"), Localization.Get("badge.save_hint"), PositiveBadgeColor);

        var (saveRow, saveGo, saveButton, saveImage, saveText) = UIFactory.CreateCompactButtonRow(
            _content, "InstallSaveButton",
            _saveInstallInProgress ? Localization.Get("save.installing") : Localization.Get("button.install_save"),
            18, 38f);
        saveImage.color = PrimaryActionBg;
        saveText.color = UIFactory.GetReadableTextColor(PrimaryActionBg);
        saveButton.interactable = !_saveInstallInProgress;
        _contentGos.Add(saveRow);
        saveButton.onClick.AddListener(() => OnInstallSaveClicked(map));

        if (SaveInstaller.FindHighestSlot() > SaveInstaller.VanillaSlotCount &&
            (!SaveInstaller.IsMoreSavesInstalled() ||
             PublicModInstaller.GetMissingDependencies(SaveInstaller.MoreSavesModName).Count > 0))
        {
            AddSpacer(4);
            AddWrappedText(Localization.Get("save.more_saves_needed"), 15, TxtNoticeYellow);
            AddRecommendedModButton("InstallMoreSavesButton", SaveInstaller.MoreSavesModName);
        }

        if (!RequiredModsChecker.IsModInstalled(TeleportMasterModName) ||
            PublicModInstaller.GetMissingDependencies(TeleportMasterModName).Count > 0)
        {
            AddSpacer(4);
            AddWrappedText(Localization.Get("save.teleport_master_hint"), 15, MutedGray);
            AddRecommendedModButton("InstallTeleportMasterButton", TeleportMasterModName);
        }

        bool hasRequiredMods = map.RequiredPublicMods != null && map.RequiredPublicMods.Count > 0;
        if (hasRequiredMods)
        {
            AddSpacer(10);
            AddBadge(Localization.Get("badge.public_mods"), Localization.Get("badge.yes"), PositiveBadgeColor);
            AddModRows(map.RequiredPublicMods
                .Select(n => (Display: n, Folder: ModFolderManager.ResolveFolderName(n), ModLinksName: n)));
        }

        AddRestartSection();
    }

    private void AddRecommendedModButton(string name, string modName)
    {
        var (row, go, button, image, text) = UIFactory.CreateCompactButtonRow(
            _content, name, Localization.Get("button.install_mod", modName), 15, 30f);
        image.color = ModsActionBg;
        text.color = UIFactory.GetReadableTextColor(ModsActionBg);
        button.interactable = !_editorActionInProgress;
        _contentGos.Add(row);
        button.onClick.AddListener(() => OnSingleModInstallClicked(modName, text));
    }

    private async void OnInstallSaveClicked(MapRow map)
    {
        if (_saveInstallInProgress) return;

        _saveInstallInProgress = true;
        RefreshContent();

        var result = await SaveInstaller.InstallAsync(map);

        _saveInstallInProgress = false;
        if (!IsSameMap(_currentMap, map)) return;

        RefreshContent();

        if (!result.Success)
        {
            ShowNotification(Localization.Get("save.failed", result.ErrorMessage), isError: true);
            return;
        }

        ShowNotification(Localization.Get("save.installed", result.Slot, result.SavePath), isError: false);

        if (map.Presets != null && map.Presets.Count > 0)
        {
            var presets = await PresetManager.DownloadPresetsAsync(map);
            if (!IsSameMap(_currentMap, map)) return;
            var activated = PresetManager.ActivatePresets(map);
            RefreshContent();
            if (presets.Failed.Count > 0)
                ShowNotification(Localization.Get("presets.download_failed", string.Join(", ", presets.Failed)), isError: true);
            if (activated.Count > 0)
                ShowNotification(Localization.Get("presets.activated", string.Join(", ", activated)), isError: false);
        }

        if (result.NeedsMoreSaves && !SaveInstaller.IsMoreSavesInstalled())
            ShowNotification(Localization.Get("save.slot_needs_more_saves", result.Slot), isError: true);
    }

    private static string AvailabilityText(IModIntegration integration)
    {
        return integration.GetAvailability() switch
        {
            IntegrationAvailability.Loaded => $"<color=#9BE89B>{Localization.Get("integrations.loaded")}</color>",
            IntegrationAvailability.Installed => $"<color=#E8D27A>{Localization.Get("integrations.installed_not_loaded")}</color>",
            _ => $"<color=#E89B9B>{Localization.Get("integrations.not_installed")}</color>"
        };
    }

    private void AddIntegrationsOverview()
    {
        AddBadge(Localization.Get("integrations.title"), "", NeutralBadgeColor);
        foreach (var integration in IntegrationRegistry.All)
            AddWrappedText($"  • {integration.DisplayName}  —  {AvailabilityText(integration)}", 15, Color.white);
    }

    private void AddMapIntegrationsSection(MapRow map)
    {
        if (map.Presets == null || map.Presets.Count == 0) return;

        AddSpacer(10);
        AddBadge(Localization.Get("integrations.title"), "", NeutralBadgeColor);

        foreach (var preset in map.Presets)
        {
            var integration = IntegrationRegistry.Find(preset.IntegrationId);
            if (integration == null) continue;

            var state = PresetManager.GetState(map, integration);
            string stateText = state switch
            {
                PresetState.Active => $"<color=#9BE89B>{Localization.Get("presets.state.active")}</color>",
                PresetState.Downloaded => $"<color=#E8D27A>{Localization.Get("presets.state.inactive")}</color>",
                _ => $"<color=#AAAAAA>{Localization.Get("presets.state.not_downloaded")}</color>"
            };

            AddWrappedText($"  • {integration.DisplayName}: {Localization.Get("presets.preset")} {stateText}  ·  " +
                           $"{Localization.Get("integrations.mod")} {AvailabilityText(integration)}", 15, Color.white);

            string buttonKey = state switch
            {
                PresetState.Active => "presets.button.disable",
                PresetState.Downloaded => "presets.button.enable",
                _ => "presets.button.download"
            };

            var (row, go, button, image, text) = UIFactory.CreateCompactButtonRow(
                _content, $"Preset_{integration.Id}", Localization.Get(buttonKey), 14, 26f, 24f, 80f);
            image.color = DarkButtonBg;
            text.color = UIFactory.GetReadableTextColor(DarkButtonBg);
            button.interactable = !_presetActionInProgress;
            _contentGos.Add(row);

            var capturedState = state;
            button.onClick.AddListener(() => OnPresetButtonClicked(map, capturedState));

            if (integration.GetAvailability() == IntegrationAvailability.NotInstalled)
                AddRecommendedModButton($"InstallIntegration_{integration.Id}", integration.ModLinksName);
        }
    }

    private bool _presetActionInProgress;

    private async void OnPresetButtonClicked(MapRow map, PresetState state)
    {
        if (_presetActionInProgress) return;

        switch (state)
        {
            case PresetState.Active:
                PresetManager.DeactivatePresets(map, automatic: false);
                RefreshContent();
                ShowNotification(Localization.Get("presets.deactivated"), isError: false);
                break;

            case PresetState.Downloaded:
                var activated = PresetManager.ActivatePresets(map, automatic: false);
                RefreshContent();
                if (activated.Count > 0)
                    ShowNotification(Localization.Get("presets.activated", string.Join(", ", activated)), isError: false);
                break;

            default:
                _presetActionInProgress = true;
                RefreshContent();
                var result = await PresetManager.DownloadPresetsAsync(map);
                _presetActionInProgress = false;
                if (!IsSameMap(_currentMap, map)) return;
                RefreshContent();
                ShowPresetDownloadResult(result);
                break;
        }
    }

    private void ShowPresetDownloadResult(MapRow map) =>
        ShowPresetDownloadResult(GlobalListAtlasMod.Instance?.DownloadManager?.TakePresetResult(map));

    private void ShowPresetDownloadResult(PresetDownloadResult result)
    {
        if (result == null) return;

        if (result.Downloaded.Count > 0)
            ShowNotification(Localization.Get("presets.downloaded", string.Join(", ", result.Downloaded)), isError: false);
        if (result.Failed.Count > 0)
            ShowNotification(Localization.Get("presets.download_failed", string.Join(", ", result.Failed)), isError: true);
    }

    private void AddBackupsButton()
    {
        var (row_go, go, button, image, text) = UIFactory.CreateCompactButtonRow(
                _content, "BackupsButton", Localization.Get("button.backups"), 16, 30f);
        image.color = DarkButtonBg;
        text.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(row_go);

        button.onClick.AddListener(() => BackupsPopup.Show(_canvasRoot, RefreshContent));
    }

    private void AddDeleteMapButton(MapRow map)
    {
        string idle = Localization.Get("delete.button_idle");
        string holding = Localization.Get("delete.button_holding");

        var (row_go, go, button, image, text) = UIFactory.CreateCompactButtonRow(
                _content, "DeleteMapButton", idle, 16, 30f);
        image.color = DeleteCrimsonBg;
        text.color = UIFactory.GetReadableTextColor(DeleteCrimsonBg);
        text.alignment = TextAnchor.MiddleCenter;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(go.transform, false);
        fillGo.transform.SetAsFirstSibling();
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        var fillImage = fillGo.GetComponent<Image>();
        fillImage.color = DeleteCrimsonFill;
        fillImage.raycastTarget = false;

        UIFactory.FitButtonWidthForLabels(go, text, 120f, idle, holding);

        var hold = go.AddComponent<HoldToConfirmButton>();
        hold.Fill = fillImage;
        hold.Label = text;
        hold.IdleLabel = idle;
        hold.HoldingLabel = holding;
        hold.OnConfirmed = () =>
        {
            var manager = GlobalListAtlasMod.Instance?.DownloadManager;
            var scenes = manager == null
                ? new List<string>()
                : SessionSceneTracker.GetDecorationMasterSceneNames(manager.GetTargetFolder(map));
            string error = manager == null
                ? "Менеджер загрузки недоступен"
                : manager.DeleteMapFiles(map);

            if (error == null) PresetManager.DeleteStoredPresets(map);
            RefreshContent();

            if (error != null)
            {
                ShowNotification(Localization.Get("delete.failed", error), isError: true);
            }
            else
            {
                ShowNotification(Localization.Get("delete.done", map.Name), isError: false);
                RefreshEditorsAfterChange(map, map.Editors, null, scenes);
            }
        };

        _contentGos.Add(row_go);
    }

    private void AddModsChecklist(List<string> neededNames, Func<string, bool> isInstalled)
    {
        if (neededNames == null || neededNames.Count == 0) return;
        AddText(Localization.Get("mods.needed"), 15, TextAnchor.MiddleLeft, MutedGray, 20);
        foreach (var name in neededNames)
            AddText($"  • {name}", 15, TextAnchor.MiddleLeft, Color.white, 20);
        var installedNames = neededNames.Where(isInstalled).ToList();
        AddText(Localization.Get("mods.installed", installedNames.Count, neededNames.Count),
            15, TextAnchor.MiddleLeft, MutedGray, 20);
        if (installedNames.Count == 0)
        {
            AddText(Localization.Get("mods.none"), 15, TextAnchor.MiddleLeft, BadRed, 20);
        }
        else
        {
            foreach (var name in installedNames)
                AddText($"  • {name}", 15, TextAnchor.MiddleLeft, GoodGreen, 20);
        }
        if (installedNames.Count == neededNames.Count)
            AddText(Localization.Get("mods.all_installed"), 16, TextAnchor.MiddleLeft, GoodGreen, 26);
    }

    private static string BuildColoredList(IEnumerable<(string label, Color32 color)> items)
    {
        return string.Join(", ", items.Select(it =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(it.color)}>{it.label}</color>"));
    }

    private void ShowNotification(string message, bool isError)
    {
        ShowNotification(message, isError ? NotificationBg : PositiveBadgeColor);
    }

    private void ShowNotification(string message, Color bgColor)
    {
        AddSpacer(10);
        var panel = UIFactory.CreatePanel(_content, "Notification", bgColor);
        var text = UIFactory.CreateText(panel, "Text", message, 16, TextAnchor.UpperLeft);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        const float horizontalPadding = 10f;
        const float verticalPadding = 6f;
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
        textRect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

        var layoutGroup = _content.GetComponent<VerticalLayoutGroup>();
        float contentHorizontalPadding = layoutGroup != null
            ? layoutGroup.padding.left + layoutGroup.padding.right
            : 0f;

        float panelWidth = _content.rect.width - contentHorizontalPadding;
        float textWidth = Mathf.Max(panelWidth - horizontalPadding * 2f, 1f);

        var generator = new TextGenerator();
        var settings = text.GetGenerationSettings(new Vector2(textWidth, 0f));
        settings.generateOutOfBounds = true;
        float preferredTextHeight = generator.GetPreferredHeight(message, settings);

        SetRowHeight(panel.gameObject, preferredTextHeight + verticalPadding * 2f);
        _contentGos.Add(panel.gameObject);

        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (_detailsScroll == null) return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        Canvas.ForceUpdateCanvases();
        _detailsScroll.verticalNormalizedPosition = 0f;

        if (isActiveAndEnabled) StartCoroutine(ScrollToBottomNextFrame());
    }

    private System.Collections.IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        if (_detailsScroll != null) _detailsScroll.verticalNormalizedPosition = 0f;
    }
    private Text AddWrappedText(string text, int fontSize, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        var t = UIFactory.CreateText(_content, "Text", text, fontSize, anchor);
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

        var layoutGroup = _content.GetComponent<VerticalLayoutGroup>();
        float contentHorizontalPadding = layoutGroup != null
            ? layoutGroup.padding.left + layoutGroup.padding.right
            : 0f;

        float textWidth = Mathf.Max(_content.rect.width - contentHorizontalPadding, 1f);

        var generator = new TextGenerator();
        var settings = t.GetGenerationSettings(new Vector2(textWidth, 0f));
        settings.generateOutOfBounds = true;
        float height = generator.GetPreferredHeight(text, settings);

        SetRowHeight(t.gameObject, height + 4f);
        _contentGos.Add(t.gameObject);
        return t;
    }

    private Text AddText(string text, int fontSize, TextAnchor anchor, Color color, float height)
    {
        var t = UIFactory.CreateText(_content, "Text", text, fontSize, anchor);
        t.color = color;
        SetRowHeight(t.gameObject, height);
        _contentGos.Add(t.gameObject);
        return t;
    }

    private void AddSpacer(float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(_content, false);
        SetRowHeight(go, height);
        _contentGos.Add(go);
    }

    private void AddBadge(string label, string value, Color bg)
    {
        var panel = UIFactory.CreatePanel(_content, $"Badge_{label}", bg);
        SetRowHeight(panel.gameObject, 32);
        var text = UIFactory.CreateText(panel, "Text", $"{label}: {value}", 16, TextAnchor.MiddleLeft);
        text.color = UIFactory.GetReadableTextColor(bg);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 0);
        textRect.offsetMax = new Vector2(-10, 0);
        _contentGos.Add(panel.gameObject);
    }

    private static void SetRowHeight(GameObject go, float height)
    {
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(0, height);
    }

    private static string StarsToString(int stars)
    {
        stars = Mathf.Clamp(stars, 0, 5);
        return new string('★', stars) + new string('☆', 5 - stars);
    }

    private void OnInstallModsClicked(MapRow map)
    {
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        if (manager == null) return;
        var result = manager.InstallAdditionalMods(map);
        if (_installModsButtonText == null) return;
        if (result.Installed > 0)
            _installModsButtonText.text = Localization.Get("mods.installed_success");
        else if (result.Skipped > 0)
            _installModsButtonText.text = Localization.Get("mods.already_installed");
        else
            _installModsButtonText.text = Localization.Get("mods.nothing_to_install");
        RefreshContent();
        if (result.Installed > 0)
        {
            ShowNotification(Localization.Get("mods.installed_restart"), isError: false);
        }
    }

    private async void OnInstallPublicModsClicked(MapRow map)
    {
        if (_installPublicModsInProgress) return;
        _installPublicModsInProgress = true;
        if (_installPublicModsButton != null) _installPublicModsButton.interactable = false;
        if (_installPublicModsButtonText != null)
            _installPublicModsButtonText.text = Localization.Get("button.installing");
        var results = await PublicModInstaller.DownloadMissingPublicModsAsync(map.RequiredPublicMods);
        _installPublicModsInProgress = false;
        if (!IsSameMap(_currentMap, map)) return;
        var actuallyInstalled = results.Where(r => r.Success && r.InstalledFolder != null).ToList();
        var failed = results.Where(r => !r.Success).ToList();
        RefreshContent();
        if (actuallyInstalled.Count > 0)
        {
            string installedList = string.Join(", ", actuallyInstalled
                .SelectMany(r => r.InstalledFolder == "dependencies"
                    ? r.InstalledDependencies
                    : new[] { r.ModName }.Concat(r.InstalledDependencies))
                .Distinct());
            ShowNotification(Localization.Get("mods.public_installed", installedList), isError: false);
        }
        if (failed.Count > 0)
        {
            string failedList = string.Join("; ", failed.Select(r => $"{r.ModName} ({r.ErrorMessage})"));
            ShowNotification(Localization.Get("mods.public_failed", failedList), isError: true);
        }
        if (actuallyInstalled.Count == 0 && failed.Count == 0)
        {
            ShowNotification(Localization.Get("mods.public_all_installed"), isError: false);
        }
    }

    private const int MaxTxtFilesShown = 5;
    private static string FormatTxtFileList(List<string> fileNames)
    {
        if (fileNames.Count <= MaxTxtFilesShown)
            return string.Join(", ", fileNames);
        string shown = string.Join(", ", fileNames.Take(MaxTxtFilesShown));
        int remaining = fileNames.Count - MaxTxtFilesShown;
        return Localization.Get("txt.and_more", shown, remaining);
    }
}