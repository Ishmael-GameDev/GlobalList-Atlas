using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Util;

namespace GlobalListAtlas.Install;

public enum ModState
{
    NotInstalled,
    Enabled,
    Disabled
}

// Общее управление модами на диске
public static class ModFolderManager
{
    public static ModState GetState(string modFolderName)
    {
        if (string.IsNullOrWhiteSpace(modFolderName))
            return ModState.NotInstalled;

        if (SafeDirExists(EnabledPath(modFolderName))) return ModState.Enabled;
        if (SafeDirExists(DisabledPath(modFolderName))) return ModState.Disabled;
        return ModState.NotInstalled;
    }

    // Включает/выключает мод переносом его папки
    public static string SetEnabled(string modFolderName, bool enable)
    {
        if (string.IsNullOrWhiteSpace(modFolderName))
            return "Не указано имя мода";

        string from = enable ? DisabledPath(modFolderName) : EnabledPath(modFolderName);
        string to = enable ? EnabledPath(modFolderName) : DisabledPath(modFolderName);

        if (!SafeDirExists(from))
            return $"Папка мода не найдена: {from}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);

            if (SafeDirExists(to))
            {
                Log.Info($"В '{to}' уже есть копия мода '{modFolderName}' — перезаписываю");
                MergeOverwrite(from, to);
                Directory.Delete(from, recursive: true);
            }
            else
            {
                Directory.Move(from, to);
            }

            Log.Info($"Мод '{modFolderName}' {(enable ? "включён" : "выключен")}");
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось {(enable ? "включить" : "выключить")} мод '{modFolderName}': {e.Message}");
            return e.Message;
        }
    }

    // Копирует дерево поверх существующего, затирая одноимённые файлы.
    // Чужие файлы в папке назначения не трогаются — удаляется только исходная папка.
    private static void MergeOverwrite(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(sourceDir))
            MergeOverwrite(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
    }

    // Переключает мод: сначала пробует прямо сейчас, а если dll заблокирована процессом игры (так бывает с загруженным модом), откладывает изменение до выхода из игры
    public static string SetEnabledOrDefer(string modFolderName, bool enable, out bool deferred)
    {
        deferred = false;

        if (string.IsNullOrWhiteSpace(modFolderName))
            return "Не указано имя мода";

        // Уже отложенное изменение просто переписываем — на диск лезть незачем
        if (PendingModChanges.IsPending(modFolderName))
        {
            PendingModChanges.Set(modFolderName, enable);
            deferred = PendingModChanges.IsPending(modFolderName);
            return null;
        }

        string error = SetEnabled(modFolderName, enable);
        if (error == null)
            return null;

        if (IsLockedError(modFolderName, enable))
        {
            PendingModChanges.Set(modFolderName, enable);
            deferred = true;
            Log.Info($"Мод '{modFolderName}' занят игрой — изменение отложено до выхода");
            return null;
        }

        return error;
    }

    // Папка на месте, но перенести не вышло — значит, файлы держит сам процесс игры
    private static bool IsLockedError(string modFolderName, bool enable)
    {
        string from = enable ? DisabledPath(modFolderName) : EnabledPath(modFolderName);
        return SafeDirExists(from);
    }

    // Имена папок всех сейчас ВКЛЮЧЁННЫХ модов (папка Disabled не считается)
    public static HashSet<string> GetEnabledModFolders()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(GamePaths.ModsFolder))
                return result;

            foreach (var dir in Directory.GetDirectories(GamePaths.ModsFolder))
            {
                string name = Path.GetFileName(dir);
                if (!string.Equals(name, "Disabled", StringComparison.OrdinalIgnoreCase))
                    result.Add(name);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось перечислить папки модов: {e.Message}");
        }

        return result;
    }

    // Имя папки мода по имени в ModLinks
    public static string ResolveFolderName(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
            return modName;

        modName = modName.Trim();
        if (GetState(modName) != ModState.NotInstalled)
            return modName;

        string wanted = Normalize(modName);

        foreach (var root in new[] { GamePaths.ModsFolder, GamePaths.DisabledModsFolder })
        {
            try
            {
                if (!Directory.Exists(root)) continue;

                var match = Directory.GetDirectories(root)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(n => Normalize(n) == wanted);

                if (match != null) return match;
            }
            catch { /* каталог недоступен — пробуем следующий */ }
        }

        return modName;
    }

    private static string Normalize(string name) =>
        new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    private static string EnabledPath(string folder) => Path.Combine(GamePaths.ModsFolder, folder);
    private static string DisabledPath(string folder) => Path.Combine(GamePaths.DisabledModsFolder, folder);

    private static bool SafeDirExists(string path)
    {
        try { return Directory.Exists(path); } catch { return false; }
    }
}
