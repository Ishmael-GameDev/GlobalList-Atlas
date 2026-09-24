using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Utility;

// Утилиты для работы с операционной системой
public static class SystemUtils
{
    // Открывает ссылку в браузере по умолчанию
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Log.Warn("Попытка открыть пустую ссылку.");
            return;
        }

        try
        {
            // Application.OpenURL сам выбирает браузер по умолчанию на всех платформах
            Application.OpenURL(url);
            Log.Info($"Открыта ссылка: {url}");
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось открыть ссылку {url}: {e.Message}");
        }
    }

    // Открывает указанную папку в системном файловом менеджере
    public static void OpenFolderInExplorer(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Log.Warn("Попытка открыть пустой путь.");
            return;
        }

        if (!Directory.Exists(path))
        {
            Log.Warn($"Папка не существует и не может быть открыта: {path}");
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // На Windows вызываем explorer
                Process.Start("explorer.exe", path.Replace('/', '\\'));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // На Mac - команда open
                Process.Start("open", $"\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // На Linux - xdg-open
                Process.Start("xdg-open", $"\"{path}\"");
            }
            else
            {
                // Фолбэк на метод Unity для неизвестных ОС
                Application.OpenURL("file://" + path);
            }

            Log.Info($"Успешно открыта папка в проводнике: {path}");
        }
        catch (Exception e)
        {
            Log.Error($"Ошибка при открытии проводника для пути {path}: {e.Message}");

            try
            {
                Application.OpenURL("file://" + path);
            }
            catch (Exception fallbackEx)
            {
                Log.Error($"Резервный метод через Unity также завершился ошибкой: {fallbackEx.Message}");
            }
        }
    }
}