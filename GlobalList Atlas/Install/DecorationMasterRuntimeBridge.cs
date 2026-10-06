using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Install;

public static class DecorationMasterRuntimeBridge
{
    private const string ModTypeName = "DecorationMaster.DecorationMaster";
    private const string SerializeHelperTypeName = "DecorationMaster.Util.SerializeHelper";
    private const string ItemSettingsTypeName = "DecorationMaster.ItemSettings";

    private static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType(fullName, false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t != null);

    public static bool Refresh(IEnumerable<string> sceneNames)
    {
        var modType = FindType(ModTypeName);
        var instance = modType?.GetField("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (instance == null)
            return false;

        try
        {
            if (modType.GetField("SceneItemData", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is IDictionary cache)
            {
                int removed = 0;
                if (sceneNames == null)
                {
                    removed = cache.Count;
                    cache.Clear();
                }
                else
                {
                    foreach (var scene in sceneNames.Where(s => !string.IsNullOrEmpty(s)))
                    {
                        if (!cache.Contains(scene)) continue;
                        cache.Remove(scene);
                        removed++;
                    }
                }

                Log.Info($"[DecorationMasterBridge] Сброшен кэш сцен: {removed}");
            }

            ReloadGlobalSettings(modType, instance);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[DecorationMasterBridge] Не удалось обновить Decoration Master: {(e.InnerException ?? e).Message}");
            return false;
        }
    }

    private static void ReloadGlobalSettings(Type modType, object instance)
    {
        var helper = FindType(SerializeHelperTypeName);
        var settingsType = FindType(ItemSettingsTypeName);
        var itemDataField = modType.GetField("ItemData", BindingFlags.Public | BindingFlags.Instance);
        var load = helper?.GetMethod("LoadGlobalSettings", BindingFlags.Public | BindingFlags.Static);
        if (settingsType == null || itemDataField == null || load == null) return;

        var global = load.MakeGenericMethod(settingsType).Invoke(null, null) ?? Activator.CreateInstance(settingsType);
        itemDataField.SetValue(instance, global);
    }
}
