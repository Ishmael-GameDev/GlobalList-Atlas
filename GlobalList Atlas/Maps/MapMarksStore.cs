using System;
using System.Collections.Generic;
using System.IO;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using Newtonsoft.Json;

namespace GlobalListAtlas.Maps;

public enum MapReaction
{
    None,
    Liked,
    Disliked
}

public static class MapMarksStore
{
    private class MarksData
    {
        public HashSet<string> Seen = new();
        public Dictionary<string, MapReaction> Reactions = new();
    }

    private static MarksData _data;

    private static string FilePath => Path.Combine(SheetConfig.GetMapsRootFolder(), "marks.json");

    public static string KeyOf(MapRow map) =>
        map.Catalog == MapCatalogKind.ArchitectServer && !string.IsNullOrEmpty(map.ServerLevelId)
            ? $"{map.Catalog}|{map.ServerLevelId}"
            : $"{map.Catalog}|{map.Name}";

    private static MarksData Data
    {
        get
        {
            if (_data != null) return _data;

            try
            {
                _data = File.Exists(FilePath)
                    ? JsonConvert.DeserializeObject<MarksData>(File.ReadAllText(FilePath)) ?? new MarksData()
                    : new MarksData();
            }
            catch (Exception e)
            {
                Log.Warn($"[Marks] Файл отметок повреждён, начинаю с пустого: {e.Message}");
                _data = new MarksData();
            }

            return _data;
        }
    }

    public static bool IsSeen(MapRow map) => map != null && Data.Seen.Contains(KeyOf(map));

    public static void SetSeen(MapRow map, bool seen)
    {
        if (map == null) return;
        if (seen) Data.Seen.Add(KeyOf(map));
        else Data.Seen.Remove(KeyOf(map));
        Save();
    }

    public static MapReaction GetReaction(MapRow map) =>
        map != null && Data.Reactions.TryGetValue(KeyOf(map), out var r) ? r : MapReaction.None;

    public static MapReaction ToggleReaction(MapRow map, MapReaction reaction)
    {
        if (map == null) return MapReaction.None;

        string key = KeyOf(map);
        var next = GetReaction(map) == reaction ? MapReaction.None : reaction;
        if (next == MapReaction.None) Data.Reactions.Remove(key);
        else Data.Reactions[key] = next;

        Save();
        return next;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Data, Formatting.Indented));
        }
        catch (Exception e)
        {
            Log.Error($"[Marks] Не удалось сохранить отметки: {e.Message}");
        }
    }
}
