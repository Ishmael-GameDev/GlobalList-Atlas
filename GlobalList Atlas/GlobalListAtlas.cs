using System;
using Modding;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.UI;
using UnityEngine;

namespace GlobalListAtlas;

public class GlobalListAtlasMod : Mod, IGlobalSettings<GlobalSettings>
{
    public static GlobalListAtlasMod Instance { get; private set; }
    private GlobalSettings _settings = new();
    public GlobalSettings Settings => _settings;
    private readonly MapDownloadManager _downloadManager = new();

    public event Action LanguageChanged;
    public GlobalListAtlasMod() : base("GlobalList Atlas") { }
    public override string GetVersion() => "1.5.0";
    public MapDownloadManager DownloadManager => _downloadManager;

    public event Action<bool> AutoSaveToggled;
    public void NotifyAutoSaveToggled(bool newState)
{
        AutoSaveToggled?.Invoke(newState);
    }
    public void SetAutoSaveCatalog(bool enabled)
    {
        _settings.IsAutoSaveCatalog = enabled;
        AutoSaveToggled?.Invoke(enabled);
        GlobalListAtlas.Logging.Log.Info($"[GlobalListAtlasMod] Автосохранение каталога: {(enabled ? "ВКЛ" : "ВЫКЛ")}");
    }

    public override void Initialize()
    {
        Instance = this;
        Install.RestartTracker.CaptureBaseline();
        Util.ModChangeScheduler.InstallQuitHook();
        Configuration.Localization.Initialize();
        Configuration.Localization.SetLanguage(
            _settings.CurrentLanguage == "ru"
                ? Configuration.Language.Russian
                : Configuration.Language.English
        );
        GlobalListAtlas.UI.MainMenuButtonManager.Initialize();

        var hostGo = new UnityEngine.GameObject("GlobalListAtlas_MapListPanelHost");
        UnityEngine.Object.DontDestroyOnLoad(hostGo);
        hostGo.AddComponent<GlobalListAtlas.UI.MapListPanel>();
        var detailsHostGo = new UnityEngine.GameObject("GlobalListAtlas_MapDetailsPanelHost");
        UnityEngine.Object.DontDestroyOnLoad(detailsHostGo);
        detailsHostGo.AddComponent<MapDetailsPanel>();
        GlobalListAtlas.Logging.Log.Info("Мод инициализирован");
    }

    public void ToggleLanguage()
    {
        _settings.CurrentLanguage = _settings.CurrentLanguage == "en" ? "ru" : "en";
        Configuration.Localization.SetLanguage(
            _settings.CurrentLanguage == "ru" ? Configuration.Language.Russian : Configuration.Language.English
        );
        GlobalListAtlas.Logging.Log.Info($"[GlobalListAtlasMod] Язык переключен на: {_settings.CurrentLanguage.ToUpper()}");

        LanguageChanged?.Invoke();
    }

    public void OnLoadGlobal(GlobalSettings settings) => _settings = settings ?? new GlobalSettings();
    public GlobalSettings OnSaveGlobal() => _settings;

    public bool ToggleButtonInsideMenu => false;

}
