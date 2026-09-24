using System;
using System.Collections.Generic;
using System.Linq;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Install;


public static class RestartTracker
{
    private static HashSet<string> _baseline;

    public static void CaptureBaseline()
    {
        _baseline = ModFolderManager.GetEnabledModFolders();
        Log.Info($"[RestartTracker] Исходно включено модов: {_baseline.Count}");
    }

    public static bool IsRestartNeeded() => GetChanges().Count > 0;

    public static List<(string ModFolder, bool EnabledNow)> GetChanges()
    {
        var changes = new List<(string, bool)>();
        if (_baseline == null)
            return changes; // baseline не снят — считаем, что изменений нет

        // Учитываем и отложенные изменения — после перезапуска они уже будут применены
        var current = PendingModChanges.GetEffectiveEnabledFolders();

        foreach (var name in current.Except(_baseline, StringComparer.OrdinalIgnoreCase))
            changes.Add((name, true));

        foreach (var name in _baseline.Except(current, StringComparer.OrdinalIgnoreCase))
            changes.Add((name, false));

        return changes;
    }

    public static string DescribeChanges()
    {
        var changes = GetChanges();
        if (changes.Count == 0) return "";

        return string.Join(", ", changes.Select(c => c.ModFolder + (c.EnabledNow ? " +" : " −")));
    }
}
