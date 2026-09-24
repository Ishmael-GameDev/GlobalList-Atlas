using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GlobalListAtlas.Install;
using GlobalListAtlas.Logging;
using UnityEngine;

namespace GlobalListAtlas.Util;

public static class ModChangeScheduler
{
    private static bool _quitHookInstalled;

    public static void InstallQuitHook()
    {
        if (_quitHookInstalled) return;
        _quitHookInstalled = true;

        Application.quitting += () =>
        {
            if (!PendingModChanges.Any) return;

            Log.Info($"[ModChangeScheduler] Выход из игры: планирую {PendingModChanges.Count} отложенных изменений");
            string error = Schedule(relaunchExePath: null);
            if (error != null)
                Log.Error($"[ModChangeScheduler] Не удалось запланировать изменения: {error}");
        };
    }

    public static string Schedule(string relaunchExePath)
    {
        var moves = PendingModChanges.GetMoves();
        if (moves.Count == 0 && relaunchExePath == null)
            return null;

        int pid = Process.GetCurrentProcess().Id;

        try
        {
            var startInfo = BuildLauncher(moves, relaunchExePath, pid);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            Process.Start(startInfo);
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"[ModChangeScheduler] Ошибка запуска планировщика: {e.Message}");
            return e.Message;
        }
    }

    private static ProcessStartInfo BuildLauncher(List<(string From, string To)> moves, string relaunchExePath, int pid)
    {
        bool windows = Application.platform == RuntimePlatform.WindowsPlayer ||
                       Application.platform == RuntimePlatform.WindowsEditor;

        return windows
            ? BuildWindowsLauncher(moves, relaunchExePath, pid)
            : BuildUnixLauncher(moves, relaunchExePath, pid);
    }

    private static ProcessStartInfo BuildWindowsLauncher(List<(string From, string To)> moves, string relaunchExePath, int pid)
    {
        var sb = new StringBuilder();
        sb.Append($"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; ");
        sb.Append("Start-Sleep -Milliseconds 800; ");

        foreach (var (from, to) in moves)
        {
            string psFrom = Ps(from);
            string psTo = Ps(to);
            // Копируем поверх и удаляем исходник: Move-Item падает, если папка назначения уже есть
            sb.Append($"if (Test-Path -LiteralPath '{psFrom}') {{ ");
            sb.Append($"New-Item -ItemType Directory -Force -Path '{psTo}' | Out-Null; ");
            sb.Append($"Copy-Item -Path '{psFrom}\\*' -Destination '{psTo}' -Recurse -Force; ");
            sb.Append($"Remove-Item -LiteralPath '{psFrom}' -Recurse -Force }}; ");
        }

        if (relaunchExePath != null)
        {
            string psExe = Ps(relaunchExePath);
            string psDir = Ps(Path.GetDirectoryName(relaunchExePath) ?? "");
            sb.Append($"Start-Process -FilePath '{psExe}' -WorkingDirectory '{psDir}'");
        }

        return new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{sb}\"");
    }

    private static ProcessStartInfo BuildUnixLauncher(List<(string From, string To)> moves, string relaunchExePath, int pid)
    {
        var sb = new StringBuilder();
        sb.Append($"while kill -0 {pid} 2>/dev/null; do sleep 0.5; done; sleep 0.5; ");

        foreach (var (from, to) in moves)
        {
            string shFrom = Sh(from);
            string shTo = Sh(to);
            sb.Append($"if [ -d \\\"{shFrom}\\\" ]; then mkdir -p \\\"{shTo}\\\"; ");
            sb.Append($"cp -R \\\"{shFrom}/.\\\" \\\"{shTo}/\\\"; rm -rf \\\"{shFrom}\\\"; fi; ");
        }

        if (relaunchExePath != null)
            sb.Append($"\\\"{Sh(relaunchExePath)}\\\" &");

        return new ProcessStartInfo("/bin/sh", $"-c \"{sb}\"");
    }

    // В PowerShell одинарные кавычки экранируются удвоением
    private static string Ps(string path) => path.Replace("'", "''");

    private static string Sh(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
