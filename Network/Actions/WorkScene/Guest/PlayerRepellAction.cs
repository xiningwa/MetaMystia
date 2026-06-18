using MemoryPack;

namespace MetaMystia.Network;

[MemoryPackable]
[AutoLog]
[RoomRelay]
public partial class PlayerRepellAction : Action
{

    public int RuntimeId { get; set; }

    [DiscardOnStory]
    [CheckScene(Common.UI.Scene.WorkScene)]
    public override void OnReceivedDerived()
    {
        var rid = RuntimeId;
        PluginManager.Instance.RunOnMainThread(() =>
        {
            var fsm = GuestsMap.GetGuestFsm(rid);
            if (fsm == null) return;
            fsm.Enqueue(nameof(GuestFSM.DoPlayerRepell),
                () => GuestFSM.DoPlayerRepell(rid));
        });
    }

    public static void Send(int runtimeId)
    {
        var action = new PlayerRepellAction()
        {
            RuntimeId = runtimeId
        };
        action.Send();
    }
}
