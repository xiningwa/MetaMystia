using MemoryPack;

using MetaMystia.Patch;

namespace MetaMystia.Network;

/// <summary>主机 → 全体玩家：确认备菜阶段全员就绪，并下发主机权威备菜表。</summary>
[MemoryPackable]
[AutoLog]
public partial class PrepAllReadyAction : Action
{
    public UpdatePrepAction.Table PrepTable { get; set; } = new();

    [CheckScene(Common.UI.Scene.IzakayaPrepScene)]
    public override void OnReceivedDerived()
    {
        if (SenderUid != MpConstants.HostUid)
        {
            Log.LogWarning($"PrepAllReady from non-host uid={SenderUid}, ignoring");
            return;
        }

        PrepSceneManager.ApplyHostTable(PrepTable);
        IzakayaConfigPannelPatch.PrepOver();
    }

    public static void Broadcast()
    {
        if (!MpManager.IsRoomHost) return;
        new PrepAllReadyAction { PrepTable = PrepSceneManager.GetLocalPrepTableSnapshot() }.Send();
    }
}
