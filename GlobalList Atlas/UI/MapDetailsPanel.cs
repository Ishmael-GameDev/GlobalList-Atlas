using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using GlobalListAtlas.Maps;
using GlobalListAtlas.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

public class MapDetailsPanel : MonoBehaviour
{
    private const float PanelAnchorMinX = MapListPanel.PanelWidthFraction;
    private const float PanelAnchorMaxX = MapListPanel.PanelWidthFraction * 3f;
    private const float PanelRightReduction = 500f;

    private static readonly Color PositiveBadgeColor = new(0.16f, 0.45f, 0.2f, 1f);
    private static readonly Color NegativeBadgeColor = new(0.45f, 0.16f, 0.16f, 1f);
    private static readonly Color NeutralBadgeColor = new(0.22f, 0.22f, 0.26f, 1f);
    private static readonly Color NotificationBg = new(0.4f, 0.32f, 0.08f, 0.95f);
    private static readonly Color MutedGray = new(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color GoodGreen = new(0.6f, 1f, 0.6f);
    private static readonly Color BadRed = new(1f, 0.6f, 0.6f);
    private static readonly Color TxtNoticeYellow = new(1f, 0.85f, 0.4f);
    private static readonly Color DownloadedYellowBg = new(0.55f, 0.47f, 0.12f, 1f);
    private static readonly Color LaunchedGreenBg = new(0.16f, 0.45f, 0.2f, 1f);
    private static readonly Color DarkButtonBg = new(0.14f, 0.14f, 0.18f, 1f);
    private static readonly Color ReinstallRedBg = new(0.55f, 0.14f, 0.14f, 1f);

    private const float ReinstallButtonHeight = 30f;
    private const float ReinstallButtonWidthFraction = 0.5f;

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
        var (content, _) = UIFactory.CreateVerticalScrollList(panel, "DetailsScroll");
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
    }

    private void RefreshContent()
    {
        if (_content == null) return;
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
        var title = AddText(map.Name, 26, TextAnchor.MiddleLeft, map.CellColor, 36);
        title.fontStyle = FontStyle.Bold;

        var editorItems = (map.Editors ?? new List<MapEditor>())
            .Select(e => (EditorConfig.GetLabel(e), (Color32)EditorConfig.GetColor(e)))
            .ToList();
        string editorsRich = editorItems.Count > 0
            ? BuildColoredList(editorItems)
            : $"<color=#AAAAAA>{Localization.Get("panel.not_specified")}</color>";
        AddText($"{Localization.Get("panel.editor")}: {editorsRich}", 18, TextAnchor.MiddleLeft, Color.white, 26);

        var tagItems = (map.Tags ?? new List<MapTag>())
            .Select(t => (TagConfig.GetLabel(t), (Color32)TagConfig.GetColor(t)))
            .ToList();
        string tagsRich = tagItems.Count > 0
            ? BuildColoredList(tagItems)
            : $"<color=#AAAAAA>{Localization.Get("panel.none")}</color>";
        AddText($"{Localization.Get("panel.tags")}: {tagsRich}", 18, TextAnchor.MiddleLeft, Color.white, 26);

        AddText($"{Localization.Get("panel.rating")}: {StarsToString(map.Stars)}", 18, TextAnchor.MiddleLeft, Color.white, 26);

        if (map.Verified)
        {
            string verifierPart = string.IsNullOrWhiteSpace(map.VerifierName)
                ? Localization.Get("panel.verified_by_unknown")
                : map.VerifierName;
            string datePart = map.VerificationDate.HasValue
                ? $" ({map.VerificationDate.Value:dd.MM.yyyy})" : "";
            AddText($"{Localization.Get("panel.verified")}: {verifierPart}{datePart}",
                18, TextAnchor.MiddleLeft, GoodGreen, 26);
        }
        else
        {
            AddText(Localization.Get("panel.not_verified"), 18, TextAnchor.MiddleLeft, BadRed, 26);
        }
        AddSpacer(10);

        bool filesAvailable = !string.IsNullOrEmpty(map.DriveUrl);
        AddBadge(Localization.Get("badge.files"),
            filesAvailable ? Localization.Get("badge.files_available") : Localization.Get("badge.files_missing"),
            filesAvailable ? PositiveBadgeColor : NegativeBadgeColor);

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
            var (cancelGo, cancelButton, cancelImage, cancelText) =
                UIFactory.CreateButton(_content, "CancelDownloadButton",
                    Localization.Get("button.cancel_download"), 16);
            SetRowHeight(cancelGo, 30);
            cancelImage.color = ReinstallRedBg;
            cancelText.color = UIFactory.GetReadableTextColor(ReinstallRedBg);
            _contentGos.Add(cancelGo);
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

            var (downloadGo, downloadButton, downloadImage, downloadText) =
                UIFactory.CreateButton(_content, "DownloadButton", downloadButtonLabel, 20);
            SetRowHeight(downloadGo, 44);
            _contentGos.Add(downloadGo);
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

        var (editorFolderGo, editorFolderButton, editorFolderImage, editorFolderText) =
            UIFactory.CreateButton(_content, "OpenEditorFolderButton",
                Localization.Get("button.open_editor_folder"), 18);
        SetRowHeight(editorFolderGo, 36);
        editorFolderImage.color = DarkButtonBg;
        editorFolderText.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(editorFolderGo);
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
                AddText(message, 16, TextAnchor.MiddleLeft, TxtNoticeYellow, 24);
            }
        }
        AddSpacer(10);

        bool hasRequiredMods = map.RequiredPublicMods != null && map.RequiredPublicMods.Count > 0;
        AddBadge(Localization.Get("badge.public_mods"),
            hasRequiredMods ? Localization.Get("badge.yes") : Localization.Get("badge.no"),
            hasRequiredMods ? PositiveBadgeColor : NeutralBadgeColor);

        if (hasRequiredMods)
        {
            AddModsChecklist(map.RequiredPublicMods, RequiredModsChecker.IsModInstalled);
            bool allPublicModsInstalled = map.RequiredPublicMods.All(RequiredModsChecker.IsModInstalled);
            if (!allPublicModsInstalled)
            {
                var (installPublicGo, installPublicButton, installPublicImage, installPublicText) =
                    UIFactory.CreateButton(_content, "InstallPublicModsButton",
                        Localization.Get("button.install_public_mods"), 20);
                SetRowHeight(installPublicGo, 44);
                _contentGos.Add(installPublicGo);
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
                AddModsChecklist(dllNames, MapFileDistributor.IsDllModInstalled);
                var (installGo, installButton, installImage, installText) =
                    UIFactory.CreateButton(_content, "InstallModsButton",
                        Localization.Get("button.install_additional_mods"), 20);
                SetRowHeight(installGo, 44);
                _contentGos.Add(installGo);
                _installModsButton = installButton;
                _installModsButtonText = installText;
                installButton.onClick.AddListener(() => OnInstallModsClicked(map));
            }
        }

        var (modsFolderGo, modsFolderButton, modsFolderImage, modsFolderText) =
            UIFactory.CreateButton(_content, "OpenModsFolderButton",
                Localization.Get("button.open_mods_folder"), 18);
        SetRowHeight(modsFolderGo, 36);
        modsFolderImage.color = DarkButtonBg;
        modsFolderText.color = UIFactory.GetReadableTextColor(DarkButtonBg);
        _contentGos.Add(modsFolderGo);
        modsFolderButton.onClick.AddListener(OnOpenModsFolderClicked);

        if (isDownloaded)
        {
            AddSpacer(16);
            var (reinstallGo, reinstallButton, reinstallImage, reinstallText) =
                UIFactory.CreateButton(_content, "ReinstallButton",
                    Localization.Get("button.reinstall"), 18);
            SetRowHeight(reinstallGo, ReinstallButtonHeight);
            var reinstallRect = (RectTransform)reinstallGo.transform;
            reinstallRect.anchorMin = new Vector2(0, reinstallRect.anchorMin.y);
            reinstallRect.anchorMax = new Vector2(ReinstallButtonWidthFraction, reinstallRect.anchorMax.y);
            reinstallImage.color = ReinstallRedBg;
            reinstallText.color = UIFactory.GetReadableTextColor(ReinstallRedBg);
            _contentGos.Add(reinstallGo);
            _reinstallButton = reinstallButton;
            _reinstallButtonText = reinstallText;
            reinstallButton.interactable = !_reinstallingMapKeys.Contains(map.Name);
            reinstallButton.onClick.AddListener(() => OnReinstallClicked(map));
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
                    foldersToOpen.Add(Path.Combine(Application.dataPath, "Managed", "Mods", "DecorationMasterData"));
                    break;
                case MapEditor.LegacyArchitect:
                case MapEditor.NewArchitect:
                    foldersToOpen.Add(Path.Combine(Application.persistentDataPath, "Architect"));
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
            string modsFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "Managed", "Mods"));
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