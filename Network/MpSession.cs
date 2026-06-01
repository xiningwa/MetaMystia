namespace MetaMystia;

public enum TransportKind
{
    None,
    DirectHost,
    DirectClient,
    RelayClient
}

public enum SyncScope
{
    None,
    Public,
    Room
}

public enum RoomRole
{
    None,
    Host,
    Client
}

public sealed class MpSession
{
    public TransportKind TransportKind { get; private set; } = TransportKind.None;
    public SyncScope SyncScope { get; private set; } = SyncScope.None;
    public RoomRole RoomRole { get; private set; } = RoomRole.None;
    public string RoomId { get; private set; } = "";
    public int HostUid { get; private set; } = MpManager.UNASSIGNED_UID;

    public bool IsOnline => TransportKind != TransportKind.None;
    public bool CanSeeOnlinePlayers => SyncScope is SyncScope.Public or SyncScope.Room;
    public bool IsInPublicScope => SyncScope == SyncScope.Public;
    public bool IsInRoom => SyncScope == SyncScope.Room;
    public bool IsRoomHost => IsInRoom && RoomRole == RoomRole.Host;
    public bool IsRoomClient => IsInRoom && RoomRole == RoomRole.Client;
    public bool IsRelayClient => TransportKind == TransportKind.RelayClient;

    public void Reset()
    {
        TransportKind = TransportKind.None;
        SyncScope = SyncScope.None;
        RoomRole = RoomRole.None;
        RoomId = "";
        HostUid = MpManager.UNASSIGNED_UID;
    }

    public void EnterDirectHostRoom()
    {
        TransportKind = TransportKind.DirectHost;
        SyncScope = SyncScope.Room;
        RoomRole = RoomRole.Host;
        RoomId = "direct";
        HostUid = MpManager.HOST_UID;
    }

    public void EnterDirectClientRoom()
    {
        TransportKind = TransportKind.DirectClient;
        SyncScope = SyncScope.Room;
        RoomRole = RoomRole.Client;
        RoomId = "direct";
        HostUid = MpManager.HOST_UID;
    }

    public void EnterRelayPublic()
    {
        TransportKind = TransportKind.RelayClient;
        SyncScope = SyncScope.Public;
        RoomRole = RoomRole.None;
        RoomId = "";
        HostUid = MpManager.UNASSIGNED_UID;
    }

    public void EnterRelayRoom(RoomRole roomRole, string roomId, int hostUid)
    {
        TransportKind = TransportKind.RelayClient;
        SyncScope = SyncScope.Room;
        RoomRole = roomRole;
        RoomId = roomId ?? "";
        HostUid = hostUid;
    }

    public void LeaveRoomToRelayPublic()
    {
        if (TransportKind != TransportKind.RelayClient)
        {
            Reset();
            return;
        }

        EnterRelayPublic();
    }
}
