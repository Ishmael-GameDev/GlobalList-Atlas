using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using GlobalListAtlas.Archive;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Util;
using UnityEngine;

namespace GlobalListAtlas.Install;

public class PublicModManifest
{
    public string Name;
    public string Version;
    public string DownloadUrl;
    public string Sha256;
    public List<string> Dependencies = new();
}

public class PublicModDownloadResult
{
    public bool Success;
    public string ErrorMessage;
    public string ModName;
    public string InstalledFolder;
    public List<string> InstalledDependencies = new();
    public List<string> FailedDependencies = new();
}

public static class PublicModInstaller
{
    private const string ModLinksUrl = "https://raw.githubusercontent.com/hk-modding/modlinks/main/ModLinks.xml";

    private const int MaxDownloadAttempts = 3;

    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

    private static List<PublicModManifest> _cachedManifests;

    public static async Task<List<PublicModManifest>> GetManifestsAsync(bool forceRefresh = false)
    {
        if (_cachedManifests != null && !forceRefresh)
            return _cachedManifests;

        Log.Info($"Скачивание ModLinks.xml: {ModLinksUrl}");

        using var client = new HttpClient();
        string xml = await client.GetStringAsync(ModLinksUrl);

        _cachedManifests = ParseManifests(xml);
        Log.Info($"ModLinks.xml разобран, найдено манифестов: {_cachedManifests.Count}");
        return _cachedManifests;
    }

    private static List<PublicModManifest> ParseManifests(string xml)
    {
        var result = new List<PublicModManifest>();

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось разобрать ModLinks.xml: {e.Message}");
            return result;
        }

        if (doc.Root == null)
            return result;

        XNamespace ns = doc.Root.GetDefaultNamespace();
        string platformTag = GetPlatformLinkTag();

        foreach (var manifestEl in doc.Root.Elements(ns + "Manifest"))
        {
            string name = manifestEl.Element(ns + "Name")?.Value?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            XElement linkEl = manifestEl.Element(ns + "Link")
                              ?? manifestEl.Element(ns + "Links")?.Element(ns + platformTag);

            result.Add(new PublicModManifest
            {
                Name = name,
                Version = manifestEl.Element(ns + "Version")?.Value?.Trim(),
                DownloadUrl = linkEl?.Value?.Trim(),
                Sha256 = linkEl?.Attribute("SHA256")?.Value?.Trim(),
                Dependencies = manifestEl.Element(ns + "Dependencies")?.Elements(ns + "Dependency")
                    .Select(d => d.Value?.Trim())
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList() ?? new List<string>()
            });
        }

        return result;
    }

    private static string GetPlatformLinkTag() => SystemInfo.operatingSystemFamily switch
    {
        OperatingSystemFamily.MacOSX => "Mac",
        OperatingSystemFamily.Linux => "Linux",
        _ => "Windows"
    };
    public static PublicModManifest GetCachedManifest(string modName)
    {
        if (_cachedManifests == null || string.IsNullOrWhiteSpace(modName))
            return null;

        return _cachedManifests.FirstOrDefault(m => string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ManifestsLoaded => _cachedManifests != null;

    public static async Task<PublicModManifest> FindManifestAsync(string modName)
    {
        var manifests = await GetManifestsAsync();
        return manifests.FirstOrDefault(m => string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
    }
    public static async Task<PublicModDownloadResult> DownloadPublicModAsync(string modName)
    {
        await GetManifestsAsync();

        var result = await InstallMissingDependenciesInternalAsync(modName);
        if (result.FailedDependencies.Count > 0)
            Log.Warn($"Мод '{modName}': не удалось поставить зависимости {string.Join(", ", result.FailedDependencies)}");

        var own = await DownloadSingleModAsync(modName);
        own.InstalledDependencies = result.InstalledDependencies;
        own.FailedDependencies = result.FailedDependencies;
        return own;
    }
    public static async Task EnableDependenciesAsync(string modName)
    {
        await GetManifestsAsync();
        foreach (var dependency in ResolveDependencyOrder(modName))
            if (ModFolderManager.GetState(dependency) == ModState.Disabled)
                ModFolderManager.SetEnabledOrDefer(dependency, true, out _);
    }

    public static async Task<PublicModDownloadResult> InstallMissingDependenciesAsync(string modName)
    {
        await GetManifestsAsync();
        var result = await InstallMissingDependenciesInternalAsync(modName);
        result.Success = result.FailedDependencies.Count == 0;
        if (!result.Success)
            result.ErrorMessage = string.Join(", ", result.FailedDependencies);
        return result;
    }
    public static List<string> GetMissingDependencies(string modName)
    {
        if (_cachedManifests == null) return new List<string>();

        return ResolveDependencyOrder(modName)
            .Where(dep => !RequiredModsChecker.IsModInstalled(dep))
            .ToList();
    }

    private static async Task<PublicModDownloadResult> InstallMissingDependenciesInternalAsync(string modName)
    {
        var result = new PublicModDownloadResult { ModName = modName };

        foreach (var dependency in ResolveDependencyOrder(modName))
        {
            if (ModFolderManager.GetState(dependency) == ModState.Disabled)
            {
                ModFolderManager.SetEnabledOrDefer(dependency, true, out _);
                result.InstalledDependencies.Add(dependency);
                continue;
            }

            if (RequiredModsChecker.IsModInstalled(dependency))
                continue;

            var depResult = await DownloadSingleModAsync(dependency);
            if (depResult.Success) result.InstalledDependencies.Add(dependency);
            else result.FailedDependencies.Add(dependency);
        }

        return result;
    }
    private static List<string> ResolveDependencyOrder(string modName)
    {
        var order = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string name, bool isRoot)
        {
            if (string.IsNullOrWhiteSpace(name) || !visited.Add(name)) return;

            var manifest = _cachedManifests?.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

            if (manifest != null)
                foreach (var dep in manifest.Dependencies)
                    Visit(dep, false);
            else if (!isRoot)
                Log.Warn($"Зависимость '{name}' не найдена в ModLinks.xml");

            if (!isRoot) order.Add(name);
        }

        Visit(modName, true);
        return order;
    }
    private static async Task<PublicModDownloadResult> DownloadSingleModAsync(string modName)
    {
        var result = new PublicModDownloadResult { ModName = modName };

        var manifest = await FindManifestAsync(modName);
        if (manifest == null)
        {
            result.ErrorMessage = $"Мод '{modName}' не найден в ModLinks.xml";
            return result;
        }

        if (string.IsNullOrEmpty(manifest.DownloadUrl))
        {
            result.ErrorMessage = $"У мода '{manifest.Name}' нет сборки для платформы {GetPlatformLinkTag()}";
            return result;
        }

        byte[] bytes = await DownloadVerifiedAsync(manifest, result);
        if (bytes == null)
            return result; // ErrorMessage уже заполнен

        string targetFolder = Path.Combine(GamePaths.ModsFolder, SanitizeFolderName(manifest.Name));

        try
        {
            if (IsZip(bytes))
            {
                bool extracted = ArchiveExtractor.ExtractToFolder(bytes, targetFolder);
                if (!extracted)
                {
                    result.ErrorMessage = $"Ошибка распаковки архива мода '{manifest.Name}'";
                    return result;
                }
            }
            else
            {
                Directory.CreateDirectory(targetFolder);
                string fileName = TryGetFileNameFromUrl(manifest.DownloadUrl) ?? $"{manifest.Name}.dll";
                File.WriteAllBytes(Path.Combine(targetFolder, fileName), bytes);
            }
        }
        catch (Exception e)
        {
            result.ErrorMessage = $"Не удалось установить файлы мода '{manifest.Name}': {e.Message}";
            return result;
        }

        result.Success = true;
        result.InstalledFolder = targetFolder;
        LumaflyRegistry.RecordInstalled(Path.GetFileName(targetFolder), manifest.Version);
        Log.Info($"Мод '{manifest.Name}' установлен: {targetFolder}");
        return result;
    }

    private static async Task<byte[]> DownloadVerifiedAsync(PublicModManifest manifest, PublicModDownloadResult result)
    {
        bool hasHash = !string.IsNullOrEmpty(manifest.Sha256);
        if (!hasHash)
            Log.Warn($"У мода '{manifest.Name}' в ModLinks.xml нет SHA256 — целостность файла проверить невозможно");

        for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            Log.Info($"Скачивание мода '{manifest.Name}' v{manifest.Version} (попытка {attempt}/{MaxDownloadAttempts}): {manifest.DownloadUrl}");

            byte[] bytes;
            try
            {
                using var client = new HttpClient();
                bytes = await client.GetByteArrayAsync(manifest.DownloadUrl);
            }
            catch (Exception e)
            {
                result.ErrorMessage = $"Не удалось скачать файл мода '{manifest.Name}': {e.Message}";
                return null;
            }

            if (!hasHash)
                return bytes;

            string actual = ComputeSha256Hex(bytes);
            if (string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                return bytes;

            Log.Warn($"SHA256 мода '{manifest.Name}' не совпадает (ожидался {manifest.Sha256}, получен {actual}), " +
                     (attempt < MaxDownloadAttempts ? "скачиваю заново" : "мод пропущен"));
        }

        result.ErrorMessage = $"Файл мода '{manifest.Name}' не прошёл проверку SHA256 после {MaxDownloadAttempts} попыток — установка отменена";
        return null;
    }

    public static async Task<List<PublicModDownloadResult>> DownloadPublicModsAsync(IEnumerable<string> modNames)
    {
        var results = new List<PublicModDownloadResult>();
        foreach (var name in modNames)
            results.Add(await DownloadPublicModAsync(name));
        return results;
    }

    public static async Task<List<PublicModDownloadResult>> DownloadMissingPublicModsAsync(IEnumerable<string> modNames)
    {
        var results = new List<PublicModDownloadResult>();

        foreach (var name in modNames)
        {
            if (RequiredModsChecker.IsModInstalled(name))
            {
                var deps = await InstallMissingDependenciesAsync(name);
                deps.Success = true;
                deps.InstalledFolder = deps.InstalledDependencies.Count > 0 ? "dependencies" : null;
                results.Add(deps);
                continue;
            }

            results.Add(await DownloadPublicModAsync(name));
        }

        return results;
    }

    private static bool IsZip(byte[] bytes)
    {
        if (bytes.Length < ZipMagic.Length)
            return false;

        for (int i = 0; i < ZipMagic.Length; i++)
            if (bytes[i] != ZipMagic[i])
                return false;

        return true;
    }

    private static string TryGetFileNameFromUrl(string url)
    {
        try
        {
            string fileName = Path.GetFileName(new Uri(url).LocalPath);
            return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
