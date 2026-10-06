using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using Newtonsoft.Json;
using UnityEngine;

namespace GlobalListAtlas.Maps;

public static class ActiveMapResolver
{
    private const double CacheSeconds = 3.0;
    private static readonly Color32 UnknownMapColor = new(0xBB, 0xBB, 0xBB, 0xFF);

    public struct ActiveMap
    {
        public MapEditor Editor;
        public string MapName;
        public Color32 Color;
    }

    private static List<ActiveMap> _cache;
    private static DateTime _cachedAt = DateTime.MinValue;

    public static void Invalidate() => _cachedAt = DateTime.MinValue;

    public static List<ActiveMap> GetActiveMaps()
    {
        if (_cache != null && (DateTime.UtcNow - _cachedAt).TotalSeconds < CacheSeconds)
            return _cache;

        var result = new List<ActiveMap>();
        var manager = GlobalListAtlasMod.Instance?.DownloadManager;
        var candidates = FindDownloadedMapFolders();

        foreach (MapEditor editor in Enum.GetValues(typeof(MapEditor)))
        {
            if (MapFileDistributor.GetEditorFolder(editor) == null) continue;

            var single = new List<MapEditor> { editor };
            foreach (var (folder, catalog) in candidates)
            {
                if (!MapFileDistributor.IsMapCurrentlyActive(folder, single)) continue;

                string folderName = Path.GetFileName(folder);
                string key = $"{catalog}|{folderName}";
                var map = manager?.FindCachedMapByFolder(catalog, folderName);
                if (map != null) Remember(key, map.Name, map.CellColor);

                if (map == null && Known.TryGetValue(key, out var known))
                {
                    result.Add(new ActiveMap
                    {
                        Editor = editor,
                        MapName = known.Name,
                        Color = new Color32(known.R, known.G, known.B, 0xFF)
                    });
                    break;
                }

                result.Add(new ActiveMap
                {
                    Editor = editor,
                    MapName = map?.Name ?? folderName,
                    Color = map?.CellColor ?? UnknownMapColor
                });
                break;
            }
        }

        SaveKnownIfChanged();
        _cache = result;
        _cachedAt = DateTime.UtcNow;
        return result;
    }

    private class KnownMap
    {
        public string Name;
        public byte R, G, B;
    }

    private static readonly string KnownPath =
        Path.Combine(Application.persistentDataPath, "GlobalListAtlas", "active_maps_cache.json");

    private static Dictionary<string, KnownMap> _known;
    private static bool _knownDirty;

    private static Dictionary<string, KnownMap> Known
    {
        get
        {
            if (_known != null) return _known;
            try
            {
                _known = File.Exists(KnownPath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, KnownMap>>(File.ReadAllText(KnownPath))
                    : null;
            }
            catch (Exception e)
            {
                Logging.Log.Warn($"[ActiveMapResolver] Кэш запущенных карт повреждён: {e.Message}");
            }
            _known ??= new Dictionary<string, KnownMap>();
            return _known;
        }
    }

    public static void RememberColor(MapCatalogKind catalog, string name, Color32 color)
    {
        Remember($"{catalog}|{name}", name, color);
        SaveKnownIfChanged();
    }

    public static Color32? GetKnownColor(MapCatalogKind catalog, string name) =>
        Known.TryGetValue($"{catalog}|{name}", out var known) ? new Color32(known.R, known.G, known.B, 0xFF) : null;

    private static void Remember(string key, string name, Color32 color)
    {
        var known = Known;
        if (known.TryGetValue(key, out var existing) &&
            existing.Name == name && existing.R == color.r && existing.G == color.g && existing.B == color.b)
            return;

        known[key] = new KnownMap { Name = name, R = color.r, G = color.g, B = color.b };
        _knownDirty = true;
    }

    private static void SaveKnownIfChanged()
    {
        if (!_knownDirty) return;
        _knownDirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KnownPath)!);
            File.WriteAllText(KnownPath, JsonConvert.SerializeObject(Known, Formatting.Indented));
        }
        catch (Exception e)
        {
            Logging.Log.Warn($"[ActiveMapResolver] Не удалось сохранить кэш запущенных карт: {e.Message}");
        }
    }

    private static List<(string Folder, MapCatalogKind Catalog)> FindDownloadedMapFolders()
    {
        var result = new List<(string, MapCatalogKind)>();
        string root = SheetConfig.GetMapsRootFolder();
        if (!Directory.Exists(root)) return result;

        string eventRoot = Path.Combine(root, "EventCommunity");
        bool IsService(string name) =>
            name.Equals("EventCommunity", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Integrations", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ArchitectServer", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_reinstall_tmp", StringComparison.OrdinalIgnoreCase);

        try
        {
            foreach (var dir in Directory.GetDirectories(root))
                if (!IsService(Path.GetFileName(dir))) result.Add((dir, MapCatalogKind.GlobalList));

            if (Directory.Exists(eventRoot))
                foreach (var dir in Directory.GetDirectories(eventRoot))
                    if (!IsService(Path.GetFileName(dir))) result.Add((dir, MapCatalogKind.EventCommunity));

            string serverRoot = Path.Combine(root, "ArchitectServer");
            if (Directory.Exists(serverRoot))
                foreach (var sourceDir in Directory.GetDirectories(serverRoot))
                    foreach (var dir in Directory.GetDirectories(sourceDir))
                        result.Add((dir, MapCatalogKind.ArchitectServer));
        }
        catch (Exception e)
        {
            Logging.Log.Warn($"[ActiveMapResolver] Не удалось перечислить скачанные карты: {e.Message}");
        }

        return result;
    }

    public static List<string> GetActiveMapBlocks()
    {
        return GetActiveMaps().Select(a =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(EditorConfig.GetColor(a.Editor))}>{EditorConfig.GetLabel(a.Editor)}</color> — " +
            $"<color=#{ColorUtility.ToHtmlStringRGB(a.Color)}>{a.MapName}</color>").ToList();
    }
}
