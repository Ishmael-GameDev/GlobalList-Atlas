using Hkmp.Api.Client;
using Hkmp.Api.Client.Networking;
using Hkmp.Api.Server;
using Hkmp.Api.Server.Networking;
using GlobalListAtlas.Install;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Integrations;

// Все типы Hkmp.* живут только в файлах Integrations/Hkmp*. Register() вызывается, только если HKMP загружен,
// поэтому без HKMP эти классы не грузятся
internal static class HkmpAddonRegistrar
{
    // HKMP при подключении сверяет имя и версию у клиента и сервера, при расхождении — отказ InvalidAddons.
    // Версия не равна версии мода, поднимать только при изменении формата пакетов
    internal const string AddonName = "GlobalListAtlas";
    internal const string AddonVersion = "1.0";

    internal static void Register()
    {
        ClientAddon.RegisterAddon(new AtlasHkmpClientAddon());
        ServerAddon.RegisterAddon(new AtlasHkmpServerAddon());
    }
}

// Отключаемый: "/addon disable GlobalListAtlas" позволяет зайти на сервер без Atlas
internal class AtlasHkmpClientAddon : TogglableClientAddon
{
    protected override string Name => HkmpAddonRegistrar.AddonName;
    protected override string Version => HkmpAddonRegistrar.AddonVersion;
    public override bool NeedsNetwork => true;

    private IClientAddonNetworkSender<ArchitectLockPacketId> _sender;

    public override void Initialize(IClientApi clientApi)
    {
        HkmpArchitectLock.HkmpUiManager = clientApi.UiManager;

        _sender = clientApi.NetClient.GetNetworkSender<ArchitectLockPacketId>(this);
        var receiver = clientApi.NetClient.GetNetworkReceiver<ArchitectLockPacketId>(this, ArchitectLockPackets.Instantiate);
        receiver.RegisterPacketHandler<ArchitectLockPacketData>(
            ArchitectLockPacketId.Policy,
            data => HkmpArchitectLock.ApplyPolicy(data.Locked, data.Version));

        var netClient = clientApi.NetClient;
        HkmpArchitectLock.ClientConnectedProvider = () => netClient.IsConnected;

        clientApi.ClientManager.ConnectEvent += () =>
        {
            HkmpArchitectLock.ChatNotifier = message => clientApi.UiManager.ChatBox.AddMessage(message);

            // Сервер шлёт блокировку сам при входе игрока, запрос снимает гонку со входом в сессию
            RequestPolicy();
            HkmpArchitectLock.PolicyRefreshRequester = RequestPolicy;
        };
        clientApi.ClientManager.DisconnectEvent += HkmpArchitectLock.ResetPolicy;
    }

    protected override void OnEnable() { }

    protected override void OnDisable() => HkmpArchitectLock.ResetPolicy();

    private void RequestPolicy() =>
        _sender?.SendSingleData(ArchitectLockPacketId.RequestPolicy, new ArchitectLockEmptyPacketData());
}

internal class AtlasHkmpServerAddon : ServerAddon
{
    protected override string Name => HkmpAddonRegistrar.AddonName;
    protected override string Version => HkmpAddonRegistrar.AddonVersion;
    public override bool NeedsNetwork => true;

    private IServerAddonNetworkSender<ArchitectLockPacketId> _sender;
    private INetServer _netServer;
    private bool _locked;
    private uint _version = 1;

    public override void Initialize(IServerApi serverApi)
    {
        _netServer = serverApi.NetServer;
        _sender = _netServer.GetNetworkSender<ArchitectLockPacketId>(this);

        var receiver = _netServer.GetNetworkReceiver<ArchitectLockPacketId>(this, ArchitectLockPackets.Instantiate);
        receiver.RegisterPacketHandler(ArchitectLockPacketId.RequestPolicy, id => SendTo(id));

        // Игроки, зашедшие после включения блокировки, получают её сразу при входе
        serverApi.ServerManager.PlayerConnectEvent += player =>
        {
            SendTo(player.Id);
            ApplyLocally();
        };

        HkmpArchitectLock.HostServerRunningProvider = () => _netServer.IsStarted;
        HkmpArchitectLock.HostLockProvider = () => _locked;
        HkmpArchitectLock.HostLockSetter = SetLocked;
        HkmpArchitectLock.HostLocalApplier = ApplyLocally;
    }

    private void SetLocked(bool locked)
    {
        if (_locked == locked) return;

        _locked = locked;
        _version++;
        Log.Info($"[HkmpArchitectLock] Хост {(locked ? "заблокировал" : "разблокировал")} Architect для всех (версия {_version})");

        // Сервер живёт в одном процессе с клиентом хоста — применяем сразу, без сети
        ApplyLocally();

        if (_netServer.IsStarted)
            _sender?.BroadcastSingleData(ArchitectLockPacketId.Policy, CreatePacket());
    }

    // Только пока наш сервер запущен: вне хостинга блокировка хоста не должна действовать на него самого
    private void ApplyLocally()
    {
        if (_netServer.IsStarted)
            HkmpArchitectLock.ApplyPolicy(_locked, _version);
    }

    private void SendTo(ushort playerId) =>
        _sender?.SendSingleData(ArchitectLockPacketId.Policy, CreatePacket(), playerId);

    private ArchitectLockPacketData CreatePacket() => new() { Locked = _locked, Version = _version };
}
