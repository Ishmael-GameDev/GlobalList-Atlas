using System;
using System.Diagnostics;
using System.IO;
using GlobalListAtlas.Logging;
using UnityEngine;

namespace GlobalListAtlas.Util;

public static class GameRestarter
{
    public static string Restart()
    {
        string exePath = TryGetExecutablePath();
        if (exePath == null)
            return "Не удалось определить путь к исполняемому файлу игры";

        string error = ModChangeScheduler.Schedule(relaunchExePath: exePath);
        if (error != null)
            return error;

        Log.Info($"[GameRestarter] Перезапуск запланирован: {exePath}");

        // Скрипт уже ждёт нашего выхода — можно закрываться
        Application.Quit();
        return null;
    }

    private static string TryGetExecutablePath()
    {
        try
        {
            string path = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;
        }
        catch (Exception e)
        {
            Log.Warn($"[GameRestarter] Не удалось получить путь процесса: {e.Message}");
        }

        return null;
    }
}
