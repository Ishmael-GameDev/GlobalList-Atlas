using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GlobalListAtlas.Install;

// Проверяет, установлены ли публичные моды, требуемые картой
public static class RequiredModsChecker
{
    private static string ModsRootFolder =>
        Path.Combine(Application.dataPath, "Managed", "Mods");

    public static List<int> GetMissingModIndices(List<string> requiredMods)
    {
        var missing = new List<int>();
        if (requiredMods == null)
            return missing;

        for (int i = 0; i < requiredMods.Count; i++)
        {
            string modFolder = Path.Combine(ModsRootFolder, requiredMods[i]);
            if (!Directory.Exists(modFolder))
                missing.Add(i);
        }

        return missing;
    }

    public static bool AllRequiredModsInstalled(List<string> requiredMods) =>
        GetMissingModIndices(requiredMods).Count == 0;
    public static bool IsModInstalled(string modName)
    {
        string modFolder = Path.Combine(ModsRootFolder, modName);
        return Directory.Exists(modFolder);
    }
}