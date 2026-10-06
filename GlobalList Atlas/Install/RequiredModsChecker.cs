using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalListAtlas.Util;
using Modding;

namespace GlobalListAtlas.Install;

public static class RequiredModsChecker
{
    public static List<int> GetMissingModIndices(List<string> requiredMods)
    {
        var missing = new List<int>();
        if (requiredMods == null)
            return missing;

        for (int i = 0; i < requiredMods.Count; i++)
        {
            if (!IsModInstalled(requiredMods[i]))
                missing.Add(i);
        }

        return missing;
    }

    public static bool AllRequiredModsInstalled(List<string> requiredMods) =>
        GetMissingModIndices(requiredMods).Count == 0;

    public static bool IsModInstalled(string modName) =>
        IsModLoaded(modName) || IsModPendingRestart(modName);

    public static bool IsModLoaded(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
            return false;

        try
        {
            return ModHooks.GetMod(modName.Trim(), onlyEnabled: false, allowLoadError: true) != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsModPendingRestart(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName) || IsModLoaded(modName))
            return false;

        string modsFolder = GamePaths.ModsFolder;
        if (!Directory.Exists(modsFolder))
            return false;

        string wanted = Normalize(modName);

        try
        {
            return Directory.GetDirectories(modsFolder)
                .Where(d => !string.Equals(Path.GetFileName(d), "Disabled", StringComparison.OrdinalIgnoreCase))
                .Any(d => Normalize(Path.GetFileName(d)) == wanted);
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string name) =>
        new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
}
