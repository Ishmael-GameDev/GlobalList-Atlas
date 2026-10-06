using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GlobalListAtlas.Drive;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;
using UnityEngine;

namespace GlobalListAtlas.Install;

public class SaveInstallResult
{
    public bool Success;
    public string ErrorMessage;
    public int Slot;
    public string SavePath;

    public bool NeedsMoreSaves => Slot > SaveInstaller.VanillaSlotCount;
}

public static class SaveInstaller
{
    public const int VanillaSlotCount = 4;
    public const string MoreSavesModName = "MoreSaves";

    private static readonly Regex SaveFileRegex = new(@"^user(\d+)\.dat$", RegexOptions.IgnoreCase);

    public static string SaveFolder => Application.persistentDataPath;

    public static int FindHighestSlot()
    {
        if (!Directory.Exists(SaveFolder)) return 0;

        return Directory.GetFiles(SaveFolder)
            .Select(Path.GetFileName)
            .Select(name => SaveFileRegex.Match(name))
            .Where(m => m.Success && int.TryParse(m.Groups[1].Value, out _))
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();
    }

    public static bool IsMoreSavesInstalled() => RequiredModsChecker.IsModInstalled(MoreSavesModName);

    public static async Task<SaveInstallResult> InstallAsync(MapRow map)
    {
        var result = new SaveInstallResult();

        if (map == null || string.IsNullOrEmpty(map.DriveUrl))
        {
            result.ErrorMessage = "У карты нет ссылки на файл сохранения";
            return result;
        }

        byte[] bytes;
        try
        {
            bytes = await GoogleDriveDownloader.DownloadAsync(map.DriveUrl);
        }
        catch (Exception e)
        {
            result.ErrorMessage = $"Не удалось скачать сохранение: {e.Message}";
            return result;
        }

        if (bytes == null || bytes.Length == 0)
        {
            result.ErrorMessage = "Не удалось скачать сохранение";
            return result;
        }

        return InstallBytes(map.Name, bytes);
    }

    public static SaveInstallResult InstallBytes(string mapName, byte[] bytes)
    {
        var result = new SaveInstallResult();
        if (bytes == null || bytes.Length == 0)
        {
            result.ErrorMessage = "Пустой файл сохранения";
            return result;
        }

        try
        {
            Directory.CreateDirectory(SaveFolder);

            result.Slot = FindHighestSlot() + 1;
            result.SavePath = Path.Combine(SaveFolder, $"user{result.Slot}.dat");

            while (File.Exists(result.SavePath))
            {
                result.Slot++;
                result.SavePath = Path.Combine(SaveFolder, $"user{result.Slot}.dat");
            }

            File.WriteAllBytes(result.SavePath, bytes);
            result.Success = true;
            Log.Info($"[SaveInstaller] Сохранение '{mapName}' записано в слот {result.Slot}: {result.SavePath}");
        }
        catch (Exception e)
        {
            result.ErrorMessage = $"Не удалось записать сохранение: {e.Message}";
            Log.Error($"[SaveInstaller] {result.ErrorMessage}");
        }

        return result;
    }
}
