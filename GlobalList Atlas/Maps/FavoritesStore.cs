using System;
using System.Linq;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Maps;

public static class FavoritesStore
{
    private static Configuration.GlobalSettings Settings => GlobalListAtlasMod.Instance?.Settings;

    public static bool IsFavorite(MapRow map)
    {
        var settings = Settings;
        if (settings?.FavoriteMaps == null || map == null) return false;

        return settings.FavoriteMaps.Any(n => string.Equals(n, map.Name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Toggle(MapRow map)
    {
        var settings = Settings;
        if (settings?.FavoriteMaps == null || map == null) return false;

        if (IsFavorite(map))
        {
            settings.FavoriteMaps.RemoveAll(n => string.Equals(n, map.Name, StringComparison.OrdinalIgnoreCase));
            Log.Info($"Карта '{map.Name}' убрана из избранного");
            return false;
        }

        settings.FavoriteMaps.Add(map.Name);
        Log.Info($"Карта '{map.Name}' добавлена в избранное");
        return true;
    }

    public static int Count => Settings?.FavoriteMaps?.Count ?? 0;
}
