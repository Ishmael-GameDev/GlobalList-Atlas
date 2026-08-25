using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GlobalListAtlas.Utility;

// Утилиты для работы с операционной системой
public static class SystemUtils
{
    // Открывает указанную папку в системном файловом менеджере
    public static void OpenFolderInExplorer(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Modding.Logger.Log("Попытка открыть пустой путь.");
            return;
        }

        if (!Directory.Exists(path))
        {
            Modding.Logger.Log($"Папка не существует и не может быть открыта: {path}");
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

            Modding.Logger.Log($"Успешно открыта папка в проводнике: {path}");
        }
        catch (Exception e)
        {
            Modding.Logger.Log($"Ошибка при открытии проводника для пути {path}: {e.Message}");

            try
            {
                Application.OpenURL("file://" + path);
            }
            catch (Exception fallbackEx)
            {
                Modding.Logger.Log($"Резервный метод через Unity также завершился ошибкой: {fallbackEx.Message}");
            }
        }
    }
}