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

// Один манифест мода из ModLinks.xml. DownloadUrl/Sha256 уже выбраны под текущую ОС.
public class PublicModManifest
{
    public string Name;
    public string Version;
    public string DownloadUrl;
    public string Sha256;
}

public class PublicModDownloadResult
{
    public bool Success;
    public string ErrorMessage;
    public string ModName;
    public string InstalledFolder;
}

public static class PublicModInstaller
{
    private const string ModLinksUrl = "https://raw.githubusercontent.com/hk-modding/modlinks/main/ModLinks.xml";

    // Сколько раз пытаться скачать файл, если его SHA256 не совпал с манифестом
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

            // Большинство модов имеют одну универсальную ссылку <Link>, но некоторые —
            // отдельные сборки под каждую ОС: <Links><Windows/><Mac/><Linux/></Links>.
            XElement linkEl = manifestEl.Element(ns + "Link")
                              ?? manifestEl.Element(ns + "Links")?.Element(ns + platformTag);

            // Манифест без ссылки под нашу платформу всё равно сохраняем (с пустым URL),
            // чтобы при установке сказать "нет сборки под вашу ОС", а не "мод не найден".
            result.Add(new PublicModManifest
            {
                Name = name,
                Version = manifestEl.Element(ns + "Version")?.Value?.Trim(),
                DownloadUrl = linkEl?.Value?.Trim(),
                Sha256 = linkEl?.Attribute("SHA256")?.Value?.Trim()
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
        Log.Info($"Мод '{manifest.Name}' установлен: {targetFolder}");
        return result;
    }

    // Скачивает файл мода и сверяет SHA256. При несовпадении перекачивает заново
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
                results.Add(new PublicModDownloadResult
                {
                    ModName = name,
                    Success = true,
                    InstalledFolder = null
                });
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
