using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Util;
using Modding;

namespace GlobalListAtlas.Install;

public enum EditorModState
{
    NotInstalled,
    Enabled,
    Disabled
}

// Реестр модов-редакторов карт: где лежат, как называются в ModLinks, в каком они сейчас состоянии и как их включать/выключать
public static class EditorModRegistry
{
    public class EditorModInfo
    {
        public MapEditor Editor;
        public string ModLinksName;   // имя мода в ModLinks.xml
        public string FolderName;     // имя папки внутри Mods
        public string DllName;        // файл, по которому редактор опознаётся
    }

    public static readonly EditorModInfo[] Entries =
    {
        new() { Editor = MapEditor.DecorationMaster, ModLinksName = "DecorationMaster", FolderName = "DecorationMaster", DllName = "DecorationMaster.dll" },
        new() { Editor = MapEditor.LegacyArchitect,  ModLinksName = "ArchitectLegacy",  FolderName = "ArchitectLegacy",  DllName = "Architect.dll" },
        new() { Editor = MapEditor.NewArchitect,     ModLinksName = "Architect",        FolderName = "Architect",        DllName = "Architect_HK.dll" },
    };

    public static EditorModInfo Find(MapEditor editor) =>
        Entries.FirstOrDefault(e => e.Editor == editor);

    // Управляемые редакторы — те, что есть в реестре (Custom Mod / Custom Engine сюда не входят)
    public static bool IsManaged(MapEditor editor) => Find(editor) != null;

    public static EditorModState GetState(MapEditor editor)
    {
        var info = Find(editor);
        if (info == null) return EditorModState.NotInstalled;

        if (Exists(EnabledPath(info))) return EditorModState.Enabled;
        if (Exists(DisabledPath(info))) return EditorModState.Disabled;
        return EditorModState.NotInstalled;
    }

    // Оба Architect'а включены одновременно — они конфликтуют между собой
    public static bool ArchitectsConflict() =>
        GetState(MapEditor.LegacyArchitect) == EditorModState.Enabled &&
        GetState(MapEditor.NewArchitect) == EditorModState.Enabled;

    // Версия установленного редактора или null
    public static string GetInstalledVersion(MapEditor editor)
    {
        var info = Find(editor);
        if (info == null) return null;

        // У обоих Architect'ов имя мода в API может совпадать
        bool ambiguous = (editor == MapEditor.LegacyArchitect || editor == MapEditor.NewArchitect)
                         && ArchitectsConflict();

        if (!ambiguous)
        {
            try
            {
                string apiVersion = ModHooks.GetMod(info.ModLinksName, onlyEnabled: false, allowLoadError: true)?.GetVersion();
                if (!string.IsNullOrWhiteSpace(apiVersion))
                    return apiVersion;
            }
            catch { /* API недоступен — идём к чтению dll */ }
        }

        string dllPath = Exists(EnabledPath(info)) ? EnabledPath(info)
                       : Exists(DisabledPath(info)) ? DisabledPath(info)
                       : null;
        if (dllPath == null) return null;

        try
        {
            // Читает только метаданные сборки, сама dll при этом не загружается
            return AssemblyName.GetAssemblyName(dllPath).Version?.ToString();
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось прочитать версию сборки {dllPath}: {e.Message}");
            return null;
        }
    }

    // Включает или выключает редактор
    public static string SetEnabled(MapEditor editor, bool enable)
    {
        var info = Find(editor);
        if (info == null)
            return $"Редактор {editor} не поддерживает включение/выключение";

        return ModFolderManager.SetEnabled(info.FolderName, enable);
    }

    private static string EnabledFolder(EditorModInfo info) => Path.Combine(GamePaths.ModsFolder, info.FolderName);
    private static string DisabledFolder(EditorModInfo info) => Path.Combine(GamePaths.DisabledModsFolder, info.FolderName);
    private static string EnabledPath(EditorModInfo info) => Path.Combine(EnabledFolder(info), info.DllName);
    private static string DisabledPath(EditorModInfo info) => Path.Combine(DisabledFolder(info), info.DllName);

    private static bool Exists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }
}
