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

    public string CurrentLanguage = "en";

    // Названия карт, отмеченных звёздочкой (столбец A таблицы)
    public List<string> FavoriteMaps = new();

}