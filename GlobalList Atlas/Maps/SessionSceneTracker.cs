using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Maps;

public static class SessionSceneTracker
{
    private static readonly HashSet<string> Visited = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> RestartReasons = new();
    private static bool _initialized;

    private static readonly Regex RoomSuffix = new(@"(_[a-c]|_bot)$", RegexOptions.IgnoreCase);

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (_, to) => Visit(to.name);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, _) => Visit(scene.name);
        Visit(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    private static void Visit(string sceneName)
    {
        if (!string.IsNullOrWhiteSpace(sceneName))
            Visited.Add(sceneName);
    }

    public static List<string> GetDecorationMasterSceneNames(string mapFolder)
    {
        if (string.IsNullOrEmpty(mapFolder) || !Directory.Exists(mapFolder))
            return new List<string>();

        return Directory.GetFiles(mapFolder, "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> FindVisited(IEnumerable<string> sceneNames)
    {
        return sceneNames
            .Where(n => Visited.Contains(n) || Visited.Contains(RoomSuffix.Replace(n, "")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void RequestRestart(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || RestartReasons.Contains(reason)) return;

        RestartReasons.Add(reason);
        Log.Info($"[SessionSceneTracker] Нужен перезапуск: {reason}");
    }

    public static bool RestartRequested => RestartReasons.Count > 0;

    public static string DescribeRestartReasons() => string.Join("; ", RestartReasons);
}