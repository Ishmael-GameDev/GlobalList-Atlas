using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using GlobalListAtlas.Logging;
using Modding;
using UnityEngine;

namespace GlobalListAtlas.Install;

// Блокировка Architect (Legacy и New) хостом HKMP для всех игроков сессии.
// Здесь нет типов Hkmp.*: всё, что связано с HKMP, приходит делегатами из Integrations/HkmpAddons.cs.
// Блокировка не сохраняется: снимается при отключении от сервера, остановке хостинга и выходе из игры
public static class HkmpArchitectLock
{
    private const string HkmpModName = "HKMP";
    private const string NewArchitectType = "Architect.Editor.EditManager";
    private const string LegacyArchitectType = "Architect.Util.EditorManager";
    private const float PolicyRefreshIntervalSeconds = 2f;

    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    // Действующая блокировка на этом клиенте (что прислал сервер)
    private static bool _locked;
    private static uint _appliedVersion;
    private static float _nextPolicyRefresh;
    private static bool _addonsRegistered;

    // Ставятся клиентским аддоном
    internal static object HkmpUiManager;
    internal static Func<bool> ClientConnectedProvider;
    internal static Action PolicyRefreshRequester;
    internal static Action<string> ChatNotifier;

    // Ставятся серверным аддоном
    internal static Func<bool> HostServerRunningProvider;
    internal static Func<bool> HostLockProvider;
    internal static Action<bool> HostLockSetter;
    internal static Action HostLocalApplier;

    private static bool _resolved;
    private static bool _toggleCheckRegistered;
    private static FieldInfo _newIsEditing;
    private static MethodInfo _newToggleEditor;
    private static FieldInfo _newToggleChecks;
    private static FieldInfo _legacyIsEditing;

    public static bool Locked => _locked;

    // Хост = у локального игрока сейчас поднят сервер HKMP (кнопка "Start Hosting")
    public static bool IsHost() => SafeRead(HostServerRunningProvider);

    // Блокировка на нашем сервере (то, что переключает кнопка хоста)
    public static bool HostLocked => SafeRead(HostLockProvider);

    public static void SetHostLocked(bool locked) => SafeInvoke(() => HostLockSetter?.Invoke(locked));

    public static bool IsHkmpLoaded()
    {
        try { return ModHooks.GetMod(HkmpModName, onlyEnabled: false, allowLoadError: false) != null; }
        catch { return false; }
    }

    // Вызывается из Initialize мода: аддоны HKMP можно зарегистрировать только во время инициализации модов
    public static void RegisterHkmpAddons()
    {
        if (_addonsRegistered || !IsHkmpLoaded()) return;
        try
        {
            Integrations.HkmpAddonRegistrar.Register();
            _addonsRegistered = true;
            Log.Info("[HkmpArchitectLock] Аддоны HKMP зарегистрированы");
        }
        catch (Exception e)
        {
            Log.Warn($"[HkmpArchitectLock] HKMP несовместимой версии, блокировка Architect недоступна: {e.Message}");
        }
    }

    internal static void ApplyPolicy(bool locked, uint version)
    {
        // HKMP может дослать потерянный старый пакет уже после нового
        if (version != 0 && version < _appliedVersion) return;
        _appliedVersion = version;

        if (_locked == locked) return;
        _locked = locked;
        Log.Info($"[HkmpArchitectLock] Architect {(locked ? "заблокирован" : "разблокирован")} хостом (версия {version})");
        SendChat(locked ? "Architect is locked by the host" : "Architect is unlocked by the host");

        if (locked) Enforce();
    }

    internal static void ResetPolicy()
    {
        _appliedVersion = 0;
        ChatNotifier = null;
        PolicyRefreshRequester = null;

        if (!_locked) return;
        _locked = false;
        Log.Info("[HkmpArchitectLock] Выход из сессии, блокировка Architect снята");
    }

    // Каждый кадр. Вне HKMP-сессии почти ничего не делает
    public static void Tick()
    {
        bool serverRunning = IsHost();

        if (serverRunning)
        {
            // Хост играет по правилам своего сервера, даже если его клиент не в сессии
            if (HostLocked != _locked)
                SafeInvoke(HostLocalApplier);
        }
        else if (HostLocked)
        {
            // Хостинг остановлен — следующий запуск сервера начинается без блокировки
            SetHostLocked(false);
        }

        if (SafeRead(ClientConnectedProvider))
        {
            // Страховка от потерянного пакета
            if (Time.unscaledTime >= _nextPolicyRefresh)
            {
                _nextPolicyRefresh = Time.unscaledTime + PolicyRefreshIntervalSeconds;
                SafeInvoke(PolicyRefreshRequester);
            }
        }
        else if (!serverRunning && _locked)
        {
            // DisconnectEvent мог не прийти
            ResetPolicy();
        }

        if (_locked) Enforce();
    }

    private static void Enforce()
    {
        Resolve();
        RegisterNewArchitectToggleCheck();
        ForceStopNewArchitect();
        ForceStopLegacyArchitect();
    }

    // Типы Architect ищем один раз: к первому вызову все моды уже загружены
    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        var newType = FindType(NewArchitectType);
        _newIsEditing = newType?.GetField("IsEditing", BindingFlags.Public | BindingFlags.Static);
        _newToggleEditor = newType?.GetMethod("ToggleEditor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
        _newToggleChecks = newType?.GetField("ToggleChecks", BindingFlags.Public | BindingFlags.Static);

        _legacyIsEditing = FindType(LegacyArchitectType)?.GetField("IsEditing", AnyStatic);
    }

    // New Architect сам даёт список условий для входа в режим редактирования — добавляем туда своё
    private static void RegisterNewArchitectToggleCheck()
    {
        if (_toggleCheckRegistered || _newToggleChecks == null) return;
        _toggleCheckRegistered = true;
        try
        {
            if (_newToggleChecks.GetValue(null) is IList checks)
                checks.Add((Func<bool>)(() => !_locked));
        }
        catch (Exception e)
        {
            Log.Warn($"[HkmpArchitectLock] Не удалось встроиться в New Architect: {e.Message}");
        }
    }

    private static void ForceStopNewArchitect()
    {
        if (_newIsEditing == null) return;
        try
        {
            if (_newIsEditing.GetValue(null) is not true) return;
            if (_newToggleEditor != null) _newToggleEditor.Invoke(null, new object[] { false });
            else _newIsEditing.SetValue(null, false);
        }
        catch (Exception e)
        {
            Log.Warn($"[HkmpArchitectLock] Не удалось отключить New Architect: {(e.InnerException ?? e).Message}");
        }
    }

    private static void ForceStopLegacyArchitect()
    {
        if (_legacyIsEditing == null) return;
        try
        {
            if (_legacyIsEditing.GetValue(null) is true)
                _legacyIsEditing.SetValue(null, false);
        }
        catch (Exception e)
        {
            Log.Warn($"[HkmpArchitectLock] Не удалось отключить Legacy Architect: {e.Message}");
        }
    }

    private static void SendChat(string message)
    {
        try { ChatNotifier?.Invoke(message); }
        catch (Exception e) { Log.Warn($"[HkmpArchitectLock] Не удалось написать в чат HKMP: {e.Message}"); }
    }

    private static bool SafeRead(Func<bool> provider)
    {
        if (provider == null) return false;
        try { return provider(); }
        catch { return false; }
    }

    private static void SafeInvoke(Action action)
    {
        if (action == null) return;
        try { action(); }
        catch (Exception e) { Log.Warn($"[HkmpArchitectLock] {e.Message}"); }
    }

    private static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType(fullName, false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t != null);
}
