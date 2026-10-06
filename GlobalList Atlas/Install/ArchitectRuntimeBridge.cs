using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GlobalListAtlas.Logging;
using Newtonsoft.Json.Linq;

namespace GlobalListAtlas.Install;

public class ArchitectRefreshResult
{
    public bool NotLoaded;
    public bool Success;
    public string ErrorMessage;
    public int AssetsDownloaded;
    public int AssetsFailed;
}

public static class ArchitectRuntimeBridge
{
    private const string StorageManagerTypeName = "Architect.Storage.StorageManager";
    private const string PreloadManagerTypeName = "Architect.Content.Preloads.PreloadManager";
    private const string PlacementManagerTypeName = "Architect.Placements.PlacementManager";
    private const string PrefabsCategoryTypeName = "Architect.Objects.Categories.PrefabsCategory";
    private const string CustomAssetManagerTypeName = "Architect.Storage.CustomAssetManager";

    private static readonly TimeSpan PreloadWaitLimit = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<string, string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png_url"] = ".png",
        ["wav_url"] = ".wav",
        ["mp4_url"] = ".mov"
    };

    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType(fullName, false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t != null);

    public static async Task<ArchitectRefreshResult> RefreshAsync(string mapFolder)
    {
        var result = new ArchitectRefreshResult();

        var storageManager = FindType(StorageManagerTypeName);
        if (storageManager == null)
        {
            result.NotLoaded = true;
            return result;
        }

        try
        {
            if (!string.IsNullOrEmpty(mapFolder))
                await DownloadExternalAssetsAsync(mapFolder, result);

            var deadline = DateTime.UtcNow + PreloadWaitLimit;
            while (IsPreloadFinished() == false && DateTime.UtcNow < deadline)
                await Task.Delay(500);

            var lateLoad = storageManager.GetMethod("LateLoad", PublicStatic, null, Type.EmptyTypes, null)
                           ?? throw new MissingMethodException("StorageManager.LateLoad");
            lateLoad.Invoke(null, null);

            ReloadPrefabs(storageManager);

            storageManager.GetMethod("LoadWorkshopData", PublicStatic, null, Type.EmptyTypes, null)?.Invoke(null, null);

            var invalidate = FindType(PlacementManagerTypeName)?.GetMethod("InvalidateScene", PublicStatic, null, Type.EmptyTypes, null)
                             ?? throw new MissingMethodException("PlacementManager.InvalidateScene");
            invalidate.Invoke(null, null);

            result.Success = true;
            Log.Info($"[ArchitectBridge] New Architect обновлён: объекты догружены, кэш сцены сброшен, ресурсов скачано {result.AssetsDownloaded}");
        }
        catch (Exception e)
        {
            result.ErrorMessage = (e.InnerException ?? e).Message;
            Log.Error($"[ArchitectBridge] Не удалось обновить New Architect: {result.ErrorMessage}");
        }

        return result;
    }

    private static bool? IsPreloadFinished() =>
        FindType(PreloadManagerTypeName)?.GetField("HasPreloaded", PublicStatic)?.GetValue(null) as bool?;

    private static void ReloadPrefabs(Type storageManager)
    {
        var dataPath = storageManager.GetField("DataPath", PublicStatic)?.GetValue(null) as string;
        var loadPrefabs = storageManager.GetMethod("LoadPrefabs", PublicStatic, null, new[] { typeof(string) }, null);
        var prefabsField = FindType(PrefabsCategoryTypeName)?.GetField("Prefabs", PublicStatic);
        if (dataPath == null || loadPrefabs == null || prefabsField == null) return;

        prefabsField.SetValue(null, loadPrefabs.Invoke(null, new object[] { dataPath }));
    }

    private static async Task DownloadExternalAssetsAsync(string mapFolder, ArchitectRefreshResult result)
    {
        var assets = FindExternalAssets(mapFolder);
        if (assets.Count == 0) return;

        var manager = FindType(CustomAssetManagerTypeName);
        var saveFile = manager?.GetMethod("SaveFile", PublicStatic, null, new[] { typeof(string), typeof(string) }, null);
        var getSavePath = manager?.GetMethod("GetSavePath", AnyStatic, null, new[] { typeof(string) }, null);
        if (saveFile == null || getSavePath == null)
        {
            result.AssetsFailed = assets.Count;
            Log.Warn("[ArchitectBridge] Методы скачивания ресурсов Architect не найдены");
            return;
        }

        foreach (var (url, extension) in assets)
        {
            try
            {
                string path = (string)getSavePath.Invoke(null, new object[] { url }) + extension;
                if (File.Exists(path)) continue; // уже скачан раньше

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bool ok = await (Task<bool>)saveFile.Invoke(null, new object[] { url, path });
                if (ok) result.AssetsDownloaded++;
                else result.AssetsFailed++;
            }
            catch (Exception e)
            {
                result.AssetsFailed++;
                Log.Warn($"[ArchitectBridge] Не удалось скачать ресурс {url}: {(e.InnerException ?? e).Message}");
            }
        }
    }

    private static List<(string Url, string Extension)> FindExternalAssets(string mapFolder)
    {
        var result = new Dictionary<string, string>();
        if (!Directory.Exists(mapFolder)) return new List<(string, string)>();

        foreach (var file in Directory.GetFiles(mapFolder, "*.architect.json", SearchOption.AllDirectories))
        {
            try
            {
                if (JToken.Parse(File.ReadAllText(file)) is not JContainer root) continue;

                foreach (var prop in root.Descendants().OfType<JProperty>())
                {
                    if (!AssetExtensions.TryGetValue(prop.Name, out var ext)) continue;
                    string url = prop.Value?.Type == JTokenType.String ? prop.Value.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(url) && !result.ContainsKey(url)) result[url] = ext;
                }
            }
            catch (Exception e)
            {
                Log.Warn($"[ArchitectBridge] Не удалось прочитать {file}: {e.Message}");
            }
        }

        return result.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }
}
