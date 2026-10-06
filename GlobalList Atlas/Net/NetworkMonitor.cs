using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Net;

public class NetworkMonitor : MonoBehaviour
{
    private static NetworkMonitor _instance;
    public static NetworkMonitor Instance => _instance;

    public static void EnsureCreated()
    {
        if (_instance == null)
        {
            Log.Info("Инициализация NetworkMonitor...");
            var go = new GameObject("[GlobalList] NetworkMonitor");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<NetworkMonitor>();
        }
    }

    private static readonly string[] ProbeUrls = new[]
    {
        "http://connectivitycheck.gstatic.com/generate_204",
        "http://www.msftconnecttest.com/connecttest.txt",
        "http://cp.cloudflare.com/generate_204"
    };

    private const float OnlineCheckIntervalSeconds = 5f;
    private const float OfflineRetryIntervalSeconds = 2f;
    private const float ProbeTimeoutSeconds = 3f;

    public bool IsOnline { get; private set; }
    public float LastPingMs { get; private set; } = -1f;

    public event Action<bool> OnlineStateChanged;

    private CancellationTokenSource _cts;

    private void Awake()
    {
        Log.Info("NetworkMonitor Awake. Запуск цикла проверки сети.");
        try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11; } catch { }
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    private void OnDestroy() => _cts?.Cancel();

    private const float GameplayCheckIntervalSeconds = 1f;

    private static bool IsMenuOpen() => UI.MapListPanel.Instance?.IsOpen == true;

    private static bool IsInGameplayScene()
    {
        try { return GameManager.instance != null && GameManager.instance.IsGameplayScene(); }
        catch { return false; }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (IsInGameplayScene() || !IsMenuOpen())
            {
                try { await Task.Delay(TimeSpan.FromSeconds(GameplayCheckIntervalSeconds), token); }
                catch (TaskCanceledException) { }
                continue;
            }

            bool wasOnline = IsOnline;
            await ProbeOnceAsync(token);

            if (IsOnline != wasOnline)
            {
                Log.Info($"Статус сети изменился: IsOnline = {IsOnline}");
                OnlineStateChanged?.Invoke(IsOnline);
            }

            float delay = IsOnline ? OnlineCheckIntervalSeconds : OfflineRetryIntervalSeconds;
            try { await Task.Delay(TimeSpan.FromSeconds(delay), token); }
            catch (TaskCanceledException) { }
        }
    }

    private async Task ProbeOnceAsync(CancellationToken token)
    {
        if (BandwidthTracker.GetBytesPerSecond() > 0)
        {
            IsOnline = true;
            if (LastPingMs <= 0) LastPingMs = 35f;
            return;
        }

        // 2. Проверка через HTTP
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds) };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        bool success = false;
        float ping = -1f;

        foreach (var url in ProbeUrls)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                sw.Stop();
                if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                {
                    success = true;
                    ping = (float)sw.Elapsed.TotalMilliseconds;
                    break;
                }
            }
            catch (Exception)
            {
            }
        }

        IsOnline = success;
        LastPingMs = success ? ping : -1f;

    }
}