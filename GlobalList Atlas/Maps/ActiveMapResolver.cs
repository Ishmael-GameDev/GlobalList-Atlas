using System;
using System.Collections.Generic;
using System.Linq;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Maps;


// Определяет, файлы какой карты сейчас лежат в папке каждого редактора
public static class ActiveMapResolver
{
    private const double CacheSeconds = 3.0;

    private static Dictionary<MapEditor, MapRow> _cache;
    private static DateTime _cachedAt = DateTime.MinValue;

    public static void Invalidate() => _cachedAt = DateTime.MinValue;

    public static Dictionary<MapEditor, MapRow> GetActiveMaps()
    {
        if (_cache != null && (DateTime.UtcNow - _cachedAt).TotalSeconds < CacheSeconds)
            return _cache;

        var result = new Dictionary<MapEditor, MapRow>();
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        var maps = manager?.CachedMaps;

        if (manager != null && maps != null)
        {
            foreach (MapEditor editor in Enum.GetValues(typeof(MapEditor)))
            {
                if (MapFileDistributor.GetEditorFolder(editor) == null)
                    continue;

                // Карта считается активной в редакторе, если её файлы лежат именно в его папке
                var single = new List<MapEditor> { editor };
                var active = maps.FirstOrDefault(m =>
                    m.Editors != null && m.Editors.Contains(editor) &&
                    manager.IsMapDownloaded(m) &&
                    MapFileDistributor.IsMapCurrentlyActive(manager.GetTargetFolder(m), single));

                if (active != null)
                    result[editor] = active;
            }
        }

        _cache = result;
        _cachedAt = DateTime.UtcNow;
        return result;
    }

    public static string DescribeActiveMaps()
    {
        var active = GetActiveMaps();
        if (active.Count == 0) return null;

        return string.Join("   ·   ", active.Select(kvp =>
        {
            string editorColor = UnityEngine.ColorUtility.ToHtmlStringRGB(EditorConfig.GetColor(kvp.Key));
            string mapColor = UnityEngine.ColorUtility.ToHtmlStringRGB(kvp.Value.CellColor);
            return $"<color=#{editorColor}>{EditorConfig.GetLabel(kvp.Key)}</color> — " +
                   $"<color=#{mapColor}>{kvp.Value.Name}</color>";
        }));
    }
}
