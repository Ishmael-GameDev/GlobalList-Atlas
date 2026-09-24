using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
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
    private bool _downloadInProgress;
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
    // Обновляет все тексты панели при смене языка
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
        if (_currentMap == null || _currentMap.Name != map.Name) return;
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
        _downloadInProgress = false;
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
        // Действия над картой — в шапке панели, а не внутри описания.
        // Справа оставлено место под кнопку закрытия, чтобы её не перекрывать.
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
        linkButton.onClick.AddListener(OnOpenSheetClicked);

        var (content, _) = UIFactory.CreateVerticalScrollList(panel, "DetailsScroll");
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
        // Небольшая кнопка справки слева от крестика
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
        helpRect.anchoredPosition = new Vector2(-(CloseButtonReserve + 4f), -9f);
        helpButton.onClick.AddListener(() => HelpPopup.Show());
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
            return;
        }

        var map = _currentMap;
        var title = AddWrappedText(map.Name, 26, map.CellColor);
        title.fontStyle = FontStyle.Bold;

        string author = string.IsNullOrWhiteSpace(map.Creator)
            ? $"<color=#AAAAAA>{Localization.Get("panel.author_unknown")}</color>"
            : map.Creator;
        AddWrappedText($"{Localization.Get("panel.author")}: {author}", 18, Color.white);

        var editorItems = (map.Editors ?? new List<MapEditor>())
            .Select(e => (EditorConfig.GetLabel(e), (Color32)EditorConfig.GetColor(e)))
            .ToList();
        string editorsRich = editorItems.Count > 0
            ? BuildColoredList(editorItems)
            : $"<color=#AAAAAA>{Localization.Get("panel.not_specified")}</color>";
        AddWrappedText($"{Localization.Get("panel.editor")}: {editorsRich}", 18, Color.white);

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
        AddSpacer(10);

        AddEditorsSection(map);

        bool filesAvailable = !string.IsNullOrEmpty(map.DriveUrl);
        string filesValue = filesAvailable
            ? Localization.Get("badge.files_available") + FormatKnownArchiveSize(map)
            : Localization.Get("badge.files_missing");
        AddBadge(Localization.Get("badge.files"), filesValue,
            filesAvailable ? PositiveBadgeColor : NegativeBadgeColor);

        // Размер узнаём заранее по заголовкам ответа Drive, без скачивания
        if (filesAvailable)
            _ = EnsureArchiveSizeAsync(map);

        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        string targetFolder = manager?.GetTargetFolder(map);
        bool isDownloaded = manager != null && manager.IsMapDownloaded(map);
        bool canLaunch = manager != null && manager.CanLaunchMap(map);
        bool isLaunched = manager != null && manager.IsMapLaunched(map);

        bool isDownloadingNow = _downloadInProgress ||
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

            AddUnloadButton(map);
            _downloadButton = downloadButton;
            _downloadButtonText = downloadText;

            bool interactable;
            if (!isDownloaded) interactable = filesAvailable && !_downloadInProgress;
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
        _downloadInProgress = true;
        RefreshContent();
        var outcome = await manager.DownloadMapFilesTrackedAsync(map);
        _downloadInProgress = false;
        if (_currentMap?.Name != map.Name) return;
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
    }

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
        if (_currentMap?.Name != map.Name) return;
        if (!outcome.Success)
        {
            if (_downloadButton != null) _downloadButton.interactable = true;
            if (_downloadButtonText != null) _downloadButtonText.text = Localization.Get("button.launch");
            ShowNotification(Localization.Get("error.generic", outcome.ErrorMessage), isError: true);
            return;
        }
        RefreshContent();
        if (outcome.MissingEditors.Count > 0)
        {
            string labels = string.Join(", ", outcome.MissingEditors.Select(EditorConfig.GetLabel));
            ShowNotification(Localization.Get("error.editor_not_installed", labels), isError: true);
        }
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
        if (_currentMap?.Name != map.Name) return;
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
    }

    // Состояние редакторов, нужных этой карте: установлен/включён, версия и кнопка действия.
    // Показываются только редакторы из EditorModRegistry (Custom Mod / Custom Engine мы не ставим).
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

        // Версии ещё не установленных редакторов берутся из ModLinks — подтягиваем
        // кэш в фоне и перерисовываем панель, когда он появится.
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

    // Включение/выключение — перенос папки мода между Mods и Mods/Disabled,
    // игра подхватывает такие изменения только при запуске.
    private void OnEditorToggleClicked(MapEditor editor, bool enable)
    {
        var info = EditorModRegistry.Find(editor);
        string error = info == null
            ? $"Редактор {editor} не поддерживает включение/выключение"
            : ModFolderManager.SetEnabledOrDefer(info.FolderName, enable, out _);
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

    // Строка одного мода: имя, состояние и кнопка действия (установить / включить / выключить).
    // modLinksName задан только для публичных модов — их можно доустановить прямо отсюда.
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

        // Неустановленные моды без имени в ModLinks ставятся общей кнопкой ниже
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

        var result = await PublicModInstaller.DownloadPublicModAsync(modLinksName);

        _editorActionInProgress = false;
        RefreshContent();

        if (result.Success)
            ShowRestartStateNotification(Localization.Get("editors.installed_restart", modLinksName));
        else
            ShowNotification(Localization.Get("editors.install_failed", modLinksName, result.ErrorMessage), isError: true);
    }

    private void ShowRestartStateNotification(string changeMessage)
    {
        if (RestartTracker.IsRestartNeeded())
            ShowNotification(changeMessage, isError: false);
        else
            ShowNotification(Localization.Get("restart.not_needed"), isError: false);
    }

    // Кнопка перезапуска: только когда состояние модов реально разошлось с исходным.
    // Срабатывает по удержанию, чтобы её нельзя было нажать случайно.
    private void AddRestartSection()
    {
        if (!RestartTracker.IsRestartNeeded()) return;

        AddSpacer(14);
        AddWrappedText(Localization.Get("restart.needed", RestartTracker.DescribeChanges()), 15, TxtNoticeYellow);

        string idle = Localization.Get("restart.button_idle");
        string holding = Localization.Get("restart.button_holding");

        var (row_go, go, button, image, text) = UIFactory.CreateCompactButtonRow(
                _content, "RestartGameButton", idle, 18, 36f);
        image.color = RestartButtonBg;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = UIFactory.GetReadableTextColor(RestartButtonBg);

        // Полоска заполнения — под текстом, поэтому добавляется первым потомком
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

        // Ширину фиксируем по самой длинной подписи, иначе кнопка "прыгает" при нажатии
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

    // Звёздочка избранного и ссылка на строку карты в самой таблице
    // Шапка живёт вне прокручиваемого содержимого, поэтому обновляется отдельно
    private void UpdateHeaderButtons()
    {
        bool hasMap = _currentMap != null;

        if (_favoriteButton != null) _favoriteButton.interactable = hasMap;
        if (_openSheetButton != null) _openSheetButton.interactable = hasMap;
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

        SystemUtils.OpenUrl(SheetConfig.GetRowUrl(_currentMap.SheetRowNumber));
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
        if (ok && _currentMap == map)
            RefreshContent();
    }

    // Выключение карты: активные файлы редактора уезжают в бекап, редактор пустеет
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
        RefreshContent();

        if (error == null)
            ShowNotification(Localization.Get("unload.done"), isError: false);
        else if (error == "NOTHING")
            ShowNotification(Localization.Get("unload.nothing"), isError: false);
        else
            ShowNotification(Localization.Get("unload.failed", error), isError: true);
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

    // Удаление скачанных файлов карты — по удержанию, чтобы не снести их случайно
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

        // Ширину фиксируем по самой длинной подписи, иначе кнопка "прыгает" при нажатии
        UIFactory.FitButtonWidthForLabels(go, text, 120f, idle, holding);

        var hold = go.AddComponent<HoldToConfirmButton>();
        hold.Fill = fillImage;
        hold.Label = text;
        hold.IdleLabel = idle;
        hold.HoldingLabel = holding;
        hold.OnConfirmed = () =>
        {
            var manager = GlobalListAtlasMod.Instance?.DownloadManager;
            // DeleteMapFiles возвращает null при успехе, поэтому отсутствие менеджера
            // проверяем отдельно — иначе успешное удаление выглядело бы как ошибка
            string error = manager == null
                ? "Менеджер загрузки недоступен"
                : manager.DeleteMapFiles(map);
            RefreshContent();

            if (error != null)
                ShowNotification(Localization.Get("delete.failed", error), isError: true);
            else
                ShowNotification(Localization.Get("delete.done", map.Name), isError: false);
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

        // Расчет высоты текста Unity-генератором под точную ширину
        var generator = new TextGenerator();
        var settings = text.GetGenerationSettings(new Vector2(textWidth, 0f));
        settings.generateOutOfBounds = true;
        float preferredTextHeight = generator.GetPreferredHeight(message, settings);

        SetRowHeight(panel.gameObject, preferredTextHeight + verticalPadding * 2f);
        _contentGos.Add(panel.gameObject);
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
        if (_currentMap?.Name != map.Name) return;
        var actuallyInstalled = results.Where(r => r.Success && r.InstalledFolder != null).ToList();
        var failed = results.Where(r => !r.Success).ToList();
        RefreshContent();
        if (actuallyInstalled.Count > 0)
        {
            string installedList = string.Join(", ", actuallyInstalled.Select(r => r.ModName));
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