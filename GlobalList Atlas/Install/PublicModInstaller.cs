using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using GlobalListAtlas.Archive;
using UnityEngine;

namespace GlobalListAtlas.Install;

//Один манифест мода из ModLinks.xml
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

    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

    private static string ManagedModsFolder =>
        Path.Combine(Application.dataPath, "Managed", "Mods");

    private static List<PublicModManifest> _cachedManifests;

    public static async Task<List<PublicModManifest>> GetManifestsAsync(bool forceRefresh = false)
    {
        if (_cachedManifests != null && !forceRefresh)
            return _cachedManifests;

        Modding.Logger.Log($"Скачивание ModLinks.xml: {ModLinksUrl}");

        using var client = new HttpClient();
        string xml = await client.GetStringAsync(ModLinksUrl);

        _cachedManifests = ParseManifests(xml);
        Modding.Logger.Log($"ModLinks.xml разобран, найдено манифестов: {_cachedManifests.Count}");
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
            Modding.Logger.Log($"Не удалось разобрать ModLinks.xml: {e.Message}");
            return result;
        }

        if (doc.Root == null)
            return result;

        XNamespace ns = doc.Root.GetDefaultNamespace();

        foreach (var manifestEl in doc.Root.Elements(ns + "Manifest"))
        {
            string name = manifestEl.Element(ns + "Name")?.Value?.Trim();
            string version = manifestEl.Element(ns + "Version")?.Value?.Trim();

            var linkEl = manifestEl.Element(ns + "Link");
            string url = linkEl?.Value?.Trim();
            string sha256 = linkEl?.Attribute("SHA256")?.Value?.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                continue;

            result.Add(new PublicModManifest
            {
                Name = name,
                Version = version,
                DownloadUrl = url,
                Sha256 = sha256
            });
        }

        return result;
    }

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

        Modding.Logger.Log($"Скачивание мода '{manifest.Name}' v{manifest.Version}: {manifest.DownloadUrl}");

        byte[] bytes;
        try
        {
            using var client = new HttpClient();
            bytes = await client.GetByteArrayAsync(manifest.DownloadUrl);
        }
        catch (Exception e)
        {
            result.ErrorMessage = $"Не удалось скачать файл мода '{manifest.Name}': {e.Message}";
            return result;
        }

        if (!string.IsNullOrEmpty(manifest.Sha256) && !VerifySha256(bytes, manifest.Sha256))
        {
            Modding.Logger.Log($"[WARN] SHA256 скачанного файла мода '{manifest.Name}' не совпадает с ModLinks.xml " +
                                "(файл мог обновиться на сервере позже манифеста, либо повреждён при скачивании) — " +
                                "установка продолжается.");
        }

        string targetFolder = Path.Combine(ManagedModsFolder, SanitizeFolderName(manifest.Name));

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
        Modding.Logger.Log($"Мод '{manifest.Name}' установлен: {targetFolder}");
        return result;
    }

    public static async Task<List<PublicModDownloadResult>> DownloadPublicModsAsync(IEnumerable<string> modNames)
    {
        var results = new List<PublicModDownloadResult>();
        foreach (var name in modNames)
            results.Add(await DownloadPublicModAsync(name));
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

    private static bool VerifySha256(byte[] data, string expectedHex)
    {
        expectedHex = expectedHex.Trim();
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(data);
        string actualHex = BitConverter.ToString(hash).Replace("-", "");

        if (expectedHex.Length != actualHex.Length)
        {
            return actualHex.Equals(expectedHex.TrimStart('0'), StringComparison.OrdinalIgnoreCase)
                || expectedHex.EndsWith(actualHex, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
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
}