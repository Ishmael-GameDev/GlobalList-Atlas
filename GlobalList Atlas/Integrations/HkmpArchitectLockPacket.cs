using Hkmp.Networking.Packet;

namespace GlobalListAtlas.Integrations;

internal enum ArchitectLockPacketId
{
    Policy = 0,
    RequestPolicy = 1
}

internal static class ArchitectLockPackets
{
    internal static IPacketData Instantiate(ArchitectLockPacketId packetId) =>
        packetId == ArchitectLockPacketId.RequestPolicy
            ? new ArchitectLockEmptyPacketData()
            : new ArchitectLockPacketData();
}

internal class ArchitectLockPacketData : IPacketData
{
    public bool Locked { get; set; }

    // Растёт при каждой смене блокировки, клиент по ней отбрасывает устаревшие переотправки
    public uint Version { get; set; }

    public bool IsReliable => true;
    public bool DropReliableDataIfNewerExists => true;

    public void WriteData(IPacket packet)
    {
        packet.Write(Locked);
        packet.Write(Version);
    }

    public void ReadData(IPacket packet)
    {
        Locked = packet.ReadBool();
        Version = packet.ReadUInt();
    }
}

internal class ArchitectLockEmptyPacketData : IPacketData
{
    public bool IsReliable => true;
    public bool DropReliableDataIfNewerExists => true;

    public void WriteData(IPacket packet) => packet.Write((byte)0);
    public void ReadData(IPacket packet) => packet.ReadByte();
}
