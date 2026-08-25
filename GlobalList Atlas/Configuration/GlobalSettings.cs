using InControl;
using Modding.Converters;
using Newtonsoft.Json;

namespace GlobalListAtlas.Configuration;

// Глобальные настройки мода
public class GlobalSettings
{

    public bool IsOfflineMode = false;
    public bool IsAutoSaveCatalog = true;

    public string CurrentLanguage = "en";

}

public class KeyBinds : PlayerActionSet
{
    public readonly PlayerAction DownloadMap;
    public readonly PlayerAction ToggleMapList;

    public KeyBinds()
    {
        DownloadMap = CreatePlayerAction("Download Map");
        DownloadMap.AddDefaultBinding(Key.F6);

        ToggleMapList = CreatePlayerAction("Toggle Map List");
        ToggleMapList.AddDefaultBinding(Key.F7);
    }
}