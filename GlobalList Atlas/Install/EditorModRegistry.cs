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

public static class EditorModRegistry
{
    public class EditorModInfo
    {
        public MapEditor Editor;
        public string ModLinksName;   // имя мода в ModLinks.xml
        public string FolderName;     // имя папки внутри Mods
        public string DllName;
    }

    public static readonly EditorModInfo[] Entries =
    {
        new() { Editor = MapEditor.DecorationMaster, ModLinksName = "DecorationMaster", FolderName = "DecorationMaster", DllName = "DecorationMaster.dll" },
        new() { Editor = MapEditor.LegacyArchitect,  ModLinksName = "ArchitectLegacy",  FolderName = "ArchitectLegacy",  DllName = "Architect.dll" },
        new() { Editor = MapEditor.NewArchitect,     ModLinksName = "Architect",        FolderName = "Architect",        DllName = "Architect_HK.dll" },
    };

    public static EditorModInfo Find(MapEditor editor) =>
        Entries.FirstOrDefault(e => e.Editor == editor);

    public static bool IsManaged(MapEditor editor) => Find(editor) != null;

    public static EditorModState GetState(MapEditor editor)
    {
        var info = Find(editor);
        if (info == null) return EditorModState.NotInstalled;

        if (Exists(EnabledPath(info))) return EditorModState.Enabled;
        if (Exists(DisabledPath(info))) return EditorModState.Disabled;
        return EditorModState.NotInstalled;
    }

    public static bool ArchitectsConflict() =>
        GetState(MapEditor.LegacyArchitect) == EditorModState.Enabled &&
        GetState(MapEditor.NewArchitect) == EditorModState.Enabled;

    public static string GetInstalledVersion(MapEditor editor)
    {
        var info = Find(editor);
        if (info == null) return null;

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
            catch {  }
        }

        string dllPath = Exists(EnabledPath(info)) ? EnabledPath(info)
                       : Exists(DisabledPath(info)) ? DisabledPath(info)
                       : null;
        if (dllPath == null) return null;

        try
        {
            return AssemblyName.GetAssemblyName(dllPath).Version?.ToString();
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось прочитать версию сборки {dllPath}: {e.Message}");
            return null;
        }
    }

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
