using MemoryPack;

using MetaMystia.UI;

namespace MetaMystia.Network;

/// <summary>
/// 主机 → 所有客机：通告玩家离开
/// </summary>
[MemoryPackable]
[AutoLog]
public partial class PeerLeaveAction : Action
{
    public int PeerUid { get; set; }

    protected override BepInEx.Logging.LogLevel OnReceiveLogLevel => BepInEx.Logging.LogLevel.Message;

    public override void OnReceivedDerived()
    {
        if (MpManager.IsRoomHost) return;

        if (PlayerManager.Peers.TryGetValue(PeerUid, out var peer))
        {
            InGameConsole.ShowPassiveFromAnyThread(TextId.PeerLeft.Get(LiveModeManager.GetDisplayName(PeerUid)));
            PlayerManager.RemovePeer(PeerUid);
        }
    }

    public static void BroadcastPeerLeave(int leavingUid)
    {
        if (!MpManager.IsRoomHost) return;
        var action = new PeerLeaveAction { PeerUid = leavingUid };
        action.Send();
    }
}
