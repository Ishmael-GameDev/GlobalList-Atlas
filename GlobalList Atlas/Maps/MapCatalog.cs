using System.Collections.Generic;

namespace GlobalListAtlas.Maps;

public static class MapCatalog
{
    public static bool TryGetByIndex(List<MapRow> maps, int oneBasedIndex, out MapRow map)
    {
        map = null;

        if (maps == null)
        {
            Modding.Logger.Log("Каталог карт ещё не загружен");
            return false;
        }

        int i = oneBasedIndex - 1;
        if (i < 0 || i >= maps.Count)
        {
            Modding.Logger.Log($"Номер карты {oneBasedIndex} вне диапазона (доступно карт: {maps.Count})");
            return false;
        }

        map = maps[i];
        return true;
    }
}