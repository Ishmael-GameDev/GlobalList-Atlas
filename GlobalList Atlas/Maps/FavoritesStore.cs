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

        return settings.FavoriteMaps.Any(n => string.Equals(n, FavoriteKey(map), StringComparison.OrdinalIgnoreCase));
    }

    public static bool Toggle(MapRow map)
    {
        var settings = Settings;
        if (settings?.FavoriteMaps == null || map == null) return false;

        if (IsFavorite(map))
        {
            settings.FavoriteMaps.RemoveAll(n => string.Equals(n, FavoriteKey(map), StringComparison.OrdinalIgnoreCase));
            Log.Info($"Карта '{map.Name}' убрана из избранного");
            return false;
        }

        settings.FavoriteMaps.Add(FavoriteKey(map));
        Log.Info($"Карта '{map.Name}' добавлена в избранное");
        return true;
    }

    private static string FavoriteKey(MapRow map) =>
        map.Catalog == Configuration.MapCatalogKind.ArchitectServer && !string.IsNullOrEmpty(map.ServerLevelId)
            ? MapMarksStore.KeyOf(map)
            : map.Name;

    public static int Count => Settings?.FavoriteMaps?.Count ?? 0;
}
