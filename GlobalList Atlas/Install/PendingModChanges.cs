using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Util;

namespace GlobalListAtlas.Install;

// Очередь изменений, которые нельзя выполнить прямо сейчас
public static class PendingModChanges
{
    // имя папки мода -> каким должно стать его состояние (true = включён)
    private static readonly Dictionary<string, bool> Pending = new(StringComparer.OrdinalIgnoreCase);

    public static int Count => Pending.Count;
    public static bool Any => Pending.Count > 0;

    public static void Set(string modFolderName, bool enable)
    {
        if (string.IsNullOrWhiteSpace(modFolderName)) return;

        // Если отложенное изменение вернуло мод к его состоянию на диске — очередь чистится
        bool onDisk = ModFolderManager.GetState(modFolderName) == ModState.Enabled;
        if (onDisk == enable)
        {
            Pending.Remove(modFolderName);
            Log.Info($"[Pending] Изменение для '{modFolderName}' отменено — состояние совпало с диском");
            return;
        }

        Pending[modFolderName] = enable;
        Log.Info($"[Pending] '{modFolderName}' будет {(enable ? "включён" : "выключен")} после выхода из игры");
    }

    public static bool TryGet(string modFolderName, out bool enable) =>
        Pending.TryGetValue(modFolderName ?? "", out enable);

    // Состояние мода с учётом отложенных изменений
    public static ModState GetEffectiveState(string modFolderName)
    {
        if (TryGet(modFolderName, out bool enable))
            return enable ? ModState.Enabled : ModState.Disabled;

        return ModFolderManager.GetState(modFolderName);
    }

    public static bool IsPending(string modFolderName) => TryGet(modFolderName, out _);

    // Набор включенных модов с учетом отложенных изменений — то, что будет после перезапуска
    public static HashSet<string> GetEffectiveEnabledFolders()
    {
        var result = ModFolderManager.GetEnabledModFolders();

        foreach (var kvp in Pending)
        {
            if (kvp.Value) result.Add(kvp.Key);
            else result.Remove(kvp.Key);
        }

        return result;
    }

    public static List<(string From, string To)> GetMoves()
    {
        return Pending.Select(kvp =>
        {
            string mods = Path.Combine(GamePaths.ModsFolder, kvp.Key);
            string disabled = Path.Combine(GamePaths.DisabledModsFolder, kvp.Key);
            return kvp.Value ? (From: disabled, To: mods) : (From: mods, To: disabled);
        }).ToList();
    }

    public static void Clear() => Pending.Clear();
}
