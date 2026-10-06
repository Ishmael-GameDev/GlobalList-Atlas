using System;
using System.IO;
using GlobalListAtlas.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GlobalListAtlas.Install;

public static class LumaflyRegistry
{
    private const string ModsKey = "Mods";
    private const string NotInModlinksKey = "NotInModlinksMods";

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HKModInstaller", "InstalledMods.json");

    public static void RecordInstalled(string folderName, string version)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return;

        Update(root =>
        {
            var mods = Section(root, ModsKey);
            var entry = mods[folderName] as JObject ?? new JObject();
            if (entry["Pinned"] == null)
                entry["Pinned"] = false;
            entry["Enabled"] = true;
            entry["Version"] = NormalizeVersion(version);
            mods[folderName] = entry;

            Section(root, NotInModlinksKey).Remove(folderName);
            return true;
        });
    }

    public static void SetEnabled(string folderName, bool enable)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return;

        Update(root =>
        {
            if (!(Section(root, ModsKey)[folderName] is JObject entry))
                return false;

            entry["Enabled"] = enable;
            return true;
        });
    }

    private static string NormalizeVersion(string version)
    {
        return Version.TryParse(version, out var parsed) ? parsed.ToString() : "0.0.0.0";
    }

    private static JObject Section(JObject root, string key)
    {
        if (root[key] is JObject section)
            return section;

        section = new JObject();
        root[key] = section;
        return section;
    }

    private static void Update(Func<JObject, bool> mutate)
    {
        string path = ConfigPath;
        try
        {
            JObject root = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();

            if (root["HasVanilla"] == null)
                root["HasVanilla"] = false;
            if (root["_ApiState"] == null)
                root["_ApiState"] = JValue.CreateNull();

            if (!mutate(root))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, root.ToString(Formatting.Indented));
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось обновить реестр Lumafly ({path}): {e.Message}");
        }
    }
}
