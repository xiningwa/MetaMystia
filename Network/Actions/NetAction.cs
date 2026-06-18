using System;
using System.Reflection;
using BepInEx.Logging;
using MemoryPack;
using SgrYuki;

namespace MetaMystia.Network;

public enum ActionType : ushort
{
    Ping,
    Pong,

    Hello,
    HelloAck,
    Reject,
    PeerJoin,
    PeerLeave,
    PlayerChangeId,
    PlayerChangeSkin,
    Message,

    SceneTransit,
    MoveSync,
    NightMoveSync,

    DayReady,
    DayAllReady,
    SelectIzakaya,
    ConfirmIzakaya,
    UpdatePrep,
    PrepReady,
    PrepAllReady,

    NightCook,
    ExtractFromCooker,
    StoreFood, // 这是往保温箱中存储，仅可以存储 food
    StoreSellable, // 这是往空位存储，可以存储 sellable（food / beverage）
    ExtractFood,
    QTE,
    Buff,

    GuestInvite,
    GuestSpawn,
    MoveToDesk,
    MoveToQueue,
    PlayerRepell,
    GenerateOrder,
    ServeSellable,
    EvaluateOrder,
    ConfirmServe,
    GuestLeave,
    SendFromQueue,
    PatientDepletedQueue,
    PatientDepletedDesk,
    GuestKill,

    FundEdit,
    TipEdit,
    ExpEdit,
    PassionEdit,

    IzakayaClose,

    // 神绮符卡的网络动作标识：上黑卡 / 下红卡
    ShinkiBlackCard,
    ShinkiRedCard,

}

[MemoryPackable]
[MemoryPackUnion((ushort)ActionType.Ping, typeof(PingAction))]
[MemoryPackUnion((ushort)ActionType.Pong, typeof(PongAction))]
[MemoryPackUnion((ushort)ActionType.Hello, typeof(HelloAction))]
[MemoryPackUnion((ushort)ActionType.HelloAck, typeof(HelloAckAction))]
[MemoryPackUnion((ushort)ActionType.Reject, typeof(RejectAction))]
[MemoryPackUnion((ushort)ActionType.PeerJoin, typeof(PeerJoinAction))]
[MemoryPackUnion((ushort)ActionType.PeerLeave, typeof(PeerLeaveAction))]
[MemoryPackUnion((ushort)ActionType.PlayerChangeId, typeof(PlayerChangeIdAction))]
[MemoryPackUnion((ushort)ActionType.PlayerChangeSkin, typeof(PlayerChangeSkinAction))]
[MemoryPackUnion((ushort)ActionType.Message, typeof(MessageAction))]
[MemoryPackUnion((ushort)ActionType.SceneTransit, typeof(SceneTransitAction))]
[MemoryPackUnion((ushort)ActionType.MoveSync, typeof(MoveSyncAction))]
[MemoryPackUnion((ushort)ActionType.NightMoveSync, typeof(NightMoveSyncAction))]
[MemoryPackUnion((ushort)ActionType.DayReady, typeof(DayReadyAction))]
[MemoryPackUnion((ushort)ActionType.DayAllReady, typeof(DayAllReadyAction))]
[MemoryPackUnion((ushort)ActionType.SelectIzakaya, typeof(SelectIzakayaAction))]
[MemoryPackUnion((ushort)ActionType.ConfirmIzakaya, typeof(ConfirmIzakayaAction))]
[MemoryPackUnion((ushort)ActionType.UpdatePrep, typeof(UpdatePrepAction))]
[MemoryPackUnion((ushort)ActionType.PrepReady, typeof(PrepReadyAction))]
[MemoryPackUnion((ushort)ActionType.PrepAllReady, typeof(PrepAllReadyAction))]
[MemoryPackUnion((ushort)ActionType.NightCook, typeof(NightCookAction))]
[MemoryPackUnion((ushort)ActionType.ExtractFromCooker, typeof(ExtractFromCookerAction))]
[MemoryPackUnion((ushort)ActionType.StoreFood, typeof(StoreFoodAction))]
[MemoryPackUnion((ushort)ActionType.StoreSellable, typeof(StoreSellableAction))]
[MemoryPackUnion((ushort)ActionType.ExtractFood, typeof(ExtractFoodAction))]
[MemoryPackUnion((ushort)ActionType.QTE, typeof(QTEAction))]
[MemoryPackUnion((ushort)ActionType.Buff, typeof(BuffAction))]
[MemoryPackUnion((ushort)ActionType.GuestInvite, typeof(GuestInviteAction))]
[MemoryPackUnion((ushort)ActionType.GuestSpawn, typeof(GuestSpawnAction))]
[MemoryPackUnion((ushort)ActionType.MoveToDesk, typeof(MoveToDeskAction))]
[MemoryPackUnion((ushort)ActionType.MoveToQueue, typeof(MoveToQueueAction))]
[MemoryPackUnion((ushort)ActionType.PlayerRepell, typeof(PlayerRepellAction))]
[MemoryPackUnion((ushort)ActionType.GenerateOrder, typeof(GenerateOrderAction))]
[MemoryPackUnion((ushort)ActionType.ServeSellable, typeof(ServeSellableAction))]
[MemoryPackUnion((ushort)ActionType.EvaluateOrder, typeof(EvaluateOrderAction))]
[MemoryPackUnion((ushort)ActionType.ConfirmServe, typeof(ConfirmServeAction))]
[MemoryPackUnion((ushort)ActionType.GuestLeave, typeof(GuestLeaveAction))]
[MemoryPackUnion((ushort)ActionType.SendFromQueue, typeof(SendFromQueueAction))]
[MemoryPackUnion((ushort)ActionType.PatientDepletedQueue, typeof(PatientDepletedQueueAction))]
[MemoryPackUnion((ushort)ActionType.PatientDepletedDesk, typeof(PatientDepletedDeskAction))]
[MemoryPackUnion((ushort)ActionType.GuestKill, typeof(GuestKillAction))]
[MemoryPackUnion((ushort)ActionType.FundEdit, typeof(FundEditAction))]
[MemoryPackUnion((ushort)ActionType.TipEdit, typeof(TipEditAction))]
[MemoryPackUnion((ushort)ActionType.ExpEdit, typeof(ExpEditAction))]
[MemoryPackUnion((ushort)ActionType.PassionEdit, typeof(PassionEditAction))]
[MemoryPackUnion((ushort)ActionType.IzakayaClose, typeof(IzakayaCloseAction))]
[MemoryPackUnion((ushort)ActionType.ShinkiBlackCard, typeof(ShinkiBlackCardAction))]
[MemoryPackUnion((ushort)ActionType.ShinkiRedCard, typeof(ShinkiRedCardAction))]

[AutoLog]

public abstract partial class Action
{
    protected long TimestampMs { get; set; }
    /// <summary>
    /// 发送者的 UID（主机=0，客机=1,2,3...）
    /// </summary>
    public int SenderUid { get; set; }

    [MemoryPackIgnore] public int? WireTargetUid { get; set; }
    [MemoryPackIgnore] public int? WireExceptUid { get; set; }

    [MemoryPackIgnore]
    protected virtual LogLevel OnReceiveLogLevel { get; } = LogLevel.Info;

    [MemoryPackIgnore]
    protected virtual LogLevel OnSendLogLevel { get; } = LogLevel.Info;

    [MemoryPackIgnore]
    protected virtual bool OnReceiveLogOnlyAction { get; } = false;

    [MemoryPackIgnore]
    protected virtual bool OnSendLogOnlyAction { get; } = false;

    protected Action()
    {
        TimestampMs = MpWire.NowMs;
        SenderUid = PlayerManager.Local.Uid;
    }


    public abstract void OnReceivedDerived();
    public void OnReceived()
    {
        LogActionReceived();
        var targetScene = GetReceivedScene();
        if (targetScene != null && MpManager.LocalScene != targetScene.Value)
        {
            Log.Info($"{MpManager.RoleTag} Received in invalid scene: {ActionName}: {ToLogString()}");
            return;
        }
        if (ShouldDiscardOnStory())
        {
            Log.Info($"{MpManager.RoleTag} Discarded (in story): {ActionName}");
            return;
        }
        OnReceivedDerived();
    }

    private Common.UI.Scene? GetReceivedScene()
    {
        var method = this.GetType().GetMethod(nameof(OnReceivedDerived));
        var attr = method.GetCustomAttribute<CheckSceneAttribute>();
        return attr?.Scene;
    }

    private bool ShouldDiscardOnStory()
    {
        if (!MpManager.InStory) return false;
        var method = this.GetType().GetMethod(nameof(OnReceivedDerived));
        return method.GetCustomAttribute<DiscardOnStoryAttribute>() != null;
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize((object)this,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                IncludeFields = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
    }

    protected virtual string ToLogString()
    {
        return ToString();
    }

    private string ActionName => GetType().Name;

    private static void LogAction(LogLevel logLevel, string logStr)
    {
        switch (logLevel)
        {
            case LogLevel.Debug:
                Log.Debug(logStr, false);
                break;
            case LogLevel.Warning:
                Log.Warning(logStr, false);
                break;
            case LogLevel.Error:
                Log.Error(logStr, false);
                break;
            case LogLevel.Fatal:
                Log.Fatal(logStr, false);
                break;
            case LogLevel.Message:
                Log.Message(logStr, false);
                break;
            default:
                Log.Info(logStr, false);
                break;
        }
    }

    protected void LogActionReceived()
    {
        string logStr = $"{MpManager.RoleTag} Received {ActionName}{(OnReceiveLogOnlyAction ? "" : $": {ToLogString()}")}";
        LogAction(OnReceiveLogLevel, logStr);
    }

    protected void LogActionSend()
    {
        string logStr = $"{MpManager.RoleTag} Send {ActionName}{(OnSendLogOnlyAction ? "" : $": {ToLogString()}")}";
        LogAction(OnSendLogLevel, logStr);
    }

    protected void Send(bool lowPriority = false)
    {
        if (!MpWire.CanSend) return;
        if (ShouldDiscardOnStory())
        {
            Log.Info($"{MpManager.RoleTag} Will not send (in story): {ActionName}");
            return;
        }
        LogActionSend();
        MpWire.EnqueueSend(this, lowPriority);
    }

    /// <summary>
    /// 标记需要房主/转发服务器转发给房间内其他玩家的 Action 类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class RoomRelayAttribute : Attribute { }

    /// <summary>Relay 公域转发标记；直连时与 <see cref="RoomRelayAttribute"/> 相同，由主机转发。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class PublicRelayAttribute : Attribute { }

    /// <summary>
    /// 旧名兼容：等价于 <see cref="RoomRelayAttribute"/>。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class ServerRelayAttribute : RoomRelayAttribute { }

    public static void RegisterAllFormatter()
    {
        if (!MemoryPackFormatterProvider.IsRegistered<Action>()) MemoryPackFormatterProvider.Register(new ActionFormatter());
        if (!MemoryPackFormatterProvider.IsRegistered<Action[]>()) MemoryPackFormatterProvider.Register(new MemoryPack.Formatters.ArrayFormatter<Action>());
    }

    [AttributeUsage(AttributeTargets.Method)]
    protected class CheckSceneAttribute(Common.UI.Scene scene) : Attribute
    {
        public Common.UI.Scene Scene { get; } = scene;
    }

    [AttributeUsage(AttributeTargets.Method)]
    protected class DiscardOnStoryAttribute : Attribute { }
}
