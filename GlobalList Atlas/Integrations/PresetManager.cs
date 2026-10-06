using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GlobalListAtlas.Archive;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Drive;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;
using Newtonsoft.Json;

namespace GlobalListAtlas.Integrations;

public enum PresetState
{
    NotDownloaded,
    Downloaded,   // скачан, но сейчас выключен
    Active        // лежит в папке мода
}

public class PresetDownloadResult
{
    public List<string> Downloaded = new();
    public List<string> Failed = new();
}

public static class PresetManager
{
    private static string RootFolder => Path.Combine(SheetConfig.GetMapsRootFolder(), "Integrations");
    private static string RegistryPath => Path.Combine(RootFolder, "active.json");

    private class ActiveEntry
    {
        public string MapKey;
        public string MapName;
        public string IntegrationId;
        public string FileName;
        public List<MapEditor> Editors = new();
    }

    private static string MapKey(MapRow map) => $"{map.Catalog}|{map.Name}";

    private static string StorageFolder(MapRow map, IModIntegration integration) =>
        Path.Combine(RootFolder, map.Catalog.ToString(), Sanitize(map.Name), integration.Id);

    private static string StoredPresetPath(MapRow map, IModIntegration integration)
    {
        string folder = StorageFolder(map, integration);
        if (!Directory.Exists(folder)) return null;

        return Directory.GetFiles(folder)
            .FirstOrDefault(f => f.EndsWith(integration.PresetExtension, StringComparison.OrdinalIgnoreCase))
            ?? Directory.GetFiles(folder).FirstOrDefault();
    }

    public static PresetState GetState(MapRow map, IModIntegration integration)
    {
        string stored = StoredPresetPath(map, integration);
        if (stored == null) return PresetState.NotDownloaded;

        bool active = LoadRegistry().Any(e => e.MapKey == MapKey(map) && e.IntegrationId == integration.Id) &&
                      File.Exists(Path.Combine(integration.ActiveFolder, Path.GetFileName(stored)));
        return active ? PresetState.Active : PresetState.Downloaded;
    }

    public static async Task<PresetDownloadResult> DownloadPresetsAsync(MapRow map)
    {
        var result = new PresetDownloadResult();
        if (map?.Presets == null) return result;

        foreach (var preset in map.Presets)
        {
            var integration = IntegrationRegistry.Find(preset.IntegrationId);
            if (integration == null || string.IsNullOrEmpty(preset.Url)) continue;

            try
            {
                await DownloadOneAsync(map, preset, integration);
                result.Downloaded.Add(integration.DisplayName);
            }
            catch (Exception e)
            {
                result.Failed.Add(integration.DisplayName);
                Log.Error($"[Presets] Пресет {integration.DisplayName} для '{map.Name}' не скачан: {e.Message}");
            }
        }

        return result;
    }

    private static async Task DownloadOneAsync(MapRow map, MapPresetRef preset, IModIntegration integration)
    {
        byte[] bytes = await GoogleDriveDownloader.DownloadAsync(preset.Url);
        if (bytes == null || bytes.Length == 0)
            throw new IOException("пустой ответ или нет доступа к файлу");

        string folder = StorageFolder(map, integration);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        if (IsArchive(bytes))
        {
            string presetFile = ExtractPresetFromArchive(bytes, integration);
            File.Copy(presetFile, Path.Combine(folder, Path.GetFileName(presetFile)), overwrite: true);
        }
        else
        {
            File.WriteAllBytes(Path.Combine(folder, ResolveFileName(map, preset, integration)), bytes);
        }

        Log.Info($"[Presets] Пресет {integration.DisplayName} для '{map.Name}' сохранён: {folder}");
    }

    private static string ExtractPresetFromArchive(byte[] bytes, IModIntegration integration)
    {
        string temp = Path.Combine(Path.GetTempPath(), "GlobalListAtlas_preset_" + Guid.NewGuid().ToString("N"));
        if (!ArchiveExtractor.ExtractToFolder(bytes, temp))
            throw new IOException("не удалось распаковать архив пресета");

        var files = Directory.GetFiles(temp, "*", SearchOption.AllDirectories);
        string match = files.FirstOrDefault(f => f.EndsWith(integration.PresetExtension, StringComparison.OrdinalIgnoreCase))
                       ?? files.FirstOrDefault(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new FileNotFoundException($"в архиве нет файла {integration.PresetExtension}");

        if (files.Count(f => f.EndsWith(integration.PresetExtension, StringComparison.OrdinalIgnoreCase)) > 1)
            Log.Warn($"[Presets] В архиве несколько пресетов {integration.DisplayName}, взят первый: {Path.GetFileName(match)}");

        string result = Path.Combine(Path.GetTempPath(), Path.GetFileName(match));
        File.Copy(match, result, overwrite: true);
        try { Directory.Delete(temp, recursive: true); } catch {  }
        return result;
    }

    private static string ResolveFileName(MapRow map, MapPresetRef preset, IModIntegration integration)
    {
        string name = preset.FileName?.Trim();
        if (!string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return name;

        return Sanitize(map.Name) + integration.PresetExtension;
    }

    public static List<string> ActivatePresets(MapRow map, bool automatic = true)
    {
        var activated = new List<string>();
        if (map?.Presets == null || map.Presets.Count == 0) return activated;

        var registry = LoadRegistry();
        var editors = map.Editors ?? new List<MapEditor>();

        foreach (var preset in map.Presets)
        {
            var integration = IntegrationRegistry.Find(preset.IntegrationId);
            if (integration == null || (automatic && !integration.IsAutoManaged)) continue;

            string stored = StoredPresetPath(map, integration);
            if (stored == null) continue;

            if (integration.IsExclusive)
            {
                foreach (var other in registry.Where(e => e.IntegrationId == integration.Id && e.MapKey != MapKey(map)).ToList())
                    DeactivateEntry(other, registry);
            }

            try
            {
                string fileName = Path.GetFileName(stored);
                string target = Path.Combine(integration.ActiveFolder, fileName);
                Directory.CreateDirectory(integration.ActiveFolder);

                bool ours = registry.Any(e => e.IntegrationId == integration.Id &&
                                              string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                if (File.Exists(target) && !ours)
                    MoveToDisabled(integration, fileName);

                File.Copy(stored, target, overwrite: true);

                registry.RemoveAll(e => e.MapKey == MapKey(map) && e.IntegrationId == integration.Id);
                registry.Add(new ActiveEntry
                {
                    MapKey = MapKey(map),
                    MapName = map.Name,
                    IntegrationId = integration.Id,
                    FileName = fileName,
                    Editors = editors.ToList()
                });

                activated.Add(integration.DisplayName);
                Log.Info($"[Presets] Пресет {integration.DisplayName} карты '{map.Name}' включён: {target}");
            }
            catch (Exception e)
            {
                Log.Error($"[Presets] Не удалось включить пресет {integration.DisplayName}: {e.Message}");
            }
        }

        SaveRegistry(registry);
        return activated;
    }

    public static void DeactivatePresets(MapRow map, bool automatic = true)
    {
        if (map == null) return;

        var registry = LoadRegistry();
        foreach (var entry in registry.Where(e => e.MapKey == MapKey(map) && (!automatic || IsAutoManaged(e))).ToList())
            DeactivateEntry(entry, registry);
        SaveRegistry(registry);
    }

    public static void DeactivateForEditors(IEnumerable<MapEditor> editors)
    {
        var set = new HashSet<MapEditor>(editors ?? Enumerable.Empty<MapEditor>());
        if (set.Count == 0) return;

        var registry = LoadRegistry();
        foreach (var entry in registry.Where(e => IsAutoManaged(e) && e.Editors.Any(set.Contains)).ToList())
            DeactivateEntry(entry, registry);
        SaveRegistry(registry);
    }

    public static bool IsAnyActive(MapRow map) =>
        map != null && LoadRegistry().Any(e => e.MapKey == MapKey(map));

    public static List<(string Integration, string MapName, MapCatalogKind Catalog)> GetExclusiveOwners()
    {
        var result = new List<(string, string, MapCatalogKind)>();
        foreach (var entry in LoadRegistry())
        {
            var integration = IntegrationRegistry.Find(entry.IntegrationId);
            if (integration == null || !integration.IsExclusive) continue;
            if (!File.Exists(Path.Combine(integration.ActiveFolder, entry.FileName))) continue;

            var parts = (entry.MapKey ?? "").Split(new[] { '|' }, 2);
            var catalog = parts.Length == 2 && Enum.TryParse(parts[0], out MapCatalogKind parsed)
                ? parsed
                : MapCatalogKind.EventCommunity;
            string name = entry.MapName ?? (parts.Length == 2 ? parts[1] : entry.MapKey);

            result.Add((integration.DisplayName, name, catalog));
        }
        return result;
    }

    private static bool IsAutoManaged(ActiveEntry entry) =>
        IntegrationRegistry.Find(entry.IntegrationId)?.IsAutoManaged ?? false;

    public static void DeleteStoredPresets(MapRow map)
    {
        DeactivatePresets(map, automatic: false);

        string folder = Path.Combine(RootFolder, map.Catalog.ToString(), Sanitize(map.Name));
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warn($"[Presets] Не удалось удалить пресеты карты '{map.Name}': {e.Message}");
        }
    }

    private static void DeactivateEntry(ActiveEntry entry, List<ActiveEntry> registry)
    {
        var integration = IntegrationRegistry.Find(entry.IntegrationId);
        if (integration != null)
            MoveToDisabled(integration, entry.FileName);

        registry.Remove(entry);
    }

    private static void MoveToDisabled(IModIntegration integration, string fileName)
    {
        string source = Path.Combine(integration.ActiveFolder, fileName);
        if (!File.Exists(source)) return;

        try
        {
            Directory.CreateDirectory(integration.DisabledFolder);
            string target = Path.Combine(integration.DisabledFolder, fileName);
            if (File.Exists(target)) File.Delete(target);
            File.Move(source, target);
            Log.Info($"[Presets] Пресет {fileName} перенесён в {integration.DisabledFolder}");
        }
        catch (Exception e)
        {
            Log.Error($"[Presets] Не удалось выключить пресет {fileName}: {e.Message}");
        }
    }

    private static List<ActiveEntry> LoadRegistry()
    {
        try
        {
            if (File.Exists(RegistryPath))
                return JsonConvert.DeserializeObject<List<ActiveEntry>>(File.ReadAllText(RegistryPath)) ?? new List<ActiveEntry>();
        }
        catch (Exception e)
        {
            Log.Warn($"[Presets] Реестр пресетов повреждён, начинаю с пустого: {e.Message}");
        }

        return new List<ActiveEntry>();
    }

    private static void SaveRegistry(List<ActiveEntry> registry)
    {
        try
        {
            Directory.CreateDirectory(RootFolder);
            File.WriteAllText(RegistryPath, JsonConvert.SerializeObject(registry, Formatting.Indented));
        }
        catch (Exception e)
        {
            Log.Error($"[Presets] Не удалось сохранить реестр пресетов: {e.Message}");
        }
    }

    private static bool IsArchive(byte[] b) =>
        b.Length >= 4 && ((b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04) ||   // zip
                          (b[0] == 0x52 && b[1] == 0x61 && b[2] == 0x72 && b[3] == 0x21) ||   // rar
                          (b.Length >= 6 && b[0] == 0x37 && b[1] == 0x7A && b[2] == 0xBC && b[3] == 0xAF)); // 7z

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
