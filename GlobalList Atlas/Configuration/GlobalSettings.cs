using System.Collections.Generic;
using InControl;
using Modding.Converters;
using Newtonsoft.Json;

namespace GlobalListAtlas.Configuration;

// Глобальные настройки мода
public class GlobalSettings
{

    public bool IsOfflineMode = false;
    public bool IsAutoSaveCatalog = true;

    [JsonIgnore]
    public bool IsDevToolsUnlocked = false;

    public string CurrentLanguage = "en";

    public List<string> FavoriteMaps = new();

}