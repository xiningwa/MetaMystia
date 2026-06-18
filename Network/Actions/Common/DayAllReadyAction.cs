using MemoryPack;

using MetaMystia.Patch;
using MetaMystia.UI;

namespace MetaMystia.Network;

/// <summary>主机 → 全体玩家：确认白天阶段全员就绪，客机收到后推进场景。</summary>
[MemoryPackable]
[AutoLog]
public partial class DayAllReadyAction : Action
{
    [CheckScene(Common.UI.Scene.DayScene)]
    public override void OnReceivedDerived()
    {
        if (SenderUid != MpConstants.HostUid)
        {
            Log.LogWarning($"DayAllReady from non-host uid={SenderUid}, ignoring");
            return;
        }

        InGameConsole.ShowPassive(TextId.AllReadyTransition.Get());
        DaySceneManagerPatch.OnDayOver();
    }

    public static void Broadcast()
    {
        if (!MpManager.IsRoomHost) return;
        new DayAllReadyAction().Send();
    }
}
