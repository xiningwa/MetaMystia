using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Attributes;
using Il2CppSystem.Collections;
using System.Collections.Generic;
using Il2CppSystem.Linq;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.Profile;
using MetaMystia.ResourceEx.SpellCollection;
using NightScene.EventUtility;
using NightScene.GuestManagementUtility;
using SgrYuki;
using SgrYuki.Utils;

namespace MetaMystia.ResourceEx.SpellCollection.Shinki;

/// <summary>
/// 神绮符卡主类：红卡「魔神降临」召唤魔界客人，黑卡「绮符·环游魔界80天」。
/// </summary>
[AutoLog]
public partial class Spell_Shinki : SpellBase
{
    internal const int ShinkiPortalBuffType = 9002;

    // 传送门视觉贴图路径：复用神绮 Buff 图标素材（rex:// 资源包内 9004_1.png）。
    private const string PortalVisualUri = "rex://ResourceExample/assets/Buff/9004_1.png";
    // 传送门屏幕水平锚点比例（0~1）：屏幕中央（不依赖相机/世界坐标）。
    private const float PortalScreenXRatio = 0.50f;
    // 传送门屏幕垂直锚点比例（0~1）：屏幕偏下区域。
    private const float PortalScreenYRatio = 0.25f;
    // 传送门向右偏移的屏幕比例：避让入口右侧，使门体不遮挡客人入场路径。
    private const float PortalScreenXOffsetRatio = 0.0875f;
    // 传送门屏幕锚点半尺寸比例：决定 UI 层门体大小。
    private const float PortalAnchorHalfSize = 0.14f;
    // 屏幕空间覆盖层排序顺序：位于游戏 UI 之下、世界之上，确保传送门恒可见且不遮挡 Buff 栏。
    private const int PortalCanvasSortingOrder = 5;
    // U9 客人召唤用的世界坐标 X 右移偏移，与视觉无关。
    private const float PortalWorldOffsetX = 1.5f;
    // U9 客人召唤世界坐标的地面层 z 分量：强制归零对齐地面，客人脚下才能找到合法 tile 起算 AStar 寻路。
    private const float PortalGroundZ = 0f;
    // U9 召唤客人时不指定目标桌位。
    private const int PortalSummonUnspecifiedDeskCode = -1;
    // 相机未就绪时 U9 客人召唤回退世界坐标，避免 null 崩溃。
    private static readonly UnityEngine.Vector3 PortalFallbackPosition = new UnityEngine.Vector3(0f, 0f, 0f);
    // 传送门 UI 图层初始透明度（0~1）。
    private static readonly UnityEngine.Color PortalInitialColor = new UnityEngine.Color(1f, 1f, 1f, 1f);
    // 传送门轴心中心（0.5, 0.5），以门体中心对齐锚点。
    private static readonly UnityEngine.Vector2 PortalCenterPivot = new UnityEngine.Vector2(0.5f, 0.5f);

    // 传送门世界坐标锚点
    internal static UnityEngine.Vector3 PortalWorldPosition = UnityEngine.Vector3.zero;
    // 传送门视觉 Canvas 句柄：红卡创建、黑卡/打烊销毁。
    private static UnityEngine.GameObject _portalVisual;

    // 魔界稀客池：集中常量，不写死散落字面量（爱丽丝/露易兹/雪/舞）。
    private static readonly List<int> MakaiSpecialGuestIds = new()
    {
        1002,  // 爱丽丝
        5005,  // 露易兹
        11000, // 雪
        11001, // 舞
    };
    // 魔界普客池：集中常量（纸牌兵/小丑）。
    private static readonly List<int> MakaiNormalGuestIds = new()
    {
        5000, // 纸牌兵
        5001, // 小丑
    };
    // 周期召唤定时器命令 id（CommandScheduler 幂等键，避免重复注册）。
    private const string PortalSummonTimerId = "ShinkiPortalSummon";
    // 周期召唤间隔（秒）：每隔该时长从传送门召唤一批魔界客人。
    private const float PortalSummonIntervalSeconds = 15f;
    // 每批召唤的客人数量。
    private const int PortalSummonBatchCount = 2;
    // 每批召唤中选取稀客的概率（其余为普客）；1/3 为既定召唤比例。
    private const float PortalSummonSpecialRatio = 1f / 3f;
    // 周期召唤是否已激活的标记：防止红卡重复触发导致定时器重复注册。
    private static bool _portalSummoningActive;

    // 黑卡动画：魔界客人从待机位走向传送门的步行时长（秒）。
    private const float BlackCardWalkDuration = 4f;
    // 黑卡动画：传送门展示（神绮淡出后留存）时长（秒），仅留极短余韵便于感知收束，随即关闭。
    private const float BlackCardPortalDisplayDuration = 0.3f;
    // 黑卡动画：移动指令的旋转参数，-1 表示沿用角色默认朝向（原生 MoveToTargetPosition 约定）。
    private const int BlackCardMoveRotationDefault = -1;
    // 黑卡动画：神绮待机位相对传送门的右下方世界偏移（portal + 此偏移），用于开门前站位，神绮最终也在此位淡出离场。
    private static readonly UnityEngine.Vector3 BlackCardShinkiStandOffset = new UnityEngine.Vector3(1.5f, -1.0f, 0f);
    // 黑卡动画：客人到达传送门后淡出的兜底超时（秒），防止个别客人卡寻路导致主协程挂死。
    private const float BlackCardGuestLeaveTimeout = 7f;

    /// <summary>
    /// 返回符卡归属角色标识，供宣言日志与立绘偏移识别使用。
    /// 标识统一取自 SpellHelper.ShinkiOwnerIdentifier，保证与立绘偏移表键一致。
    /// </summary>
    /// <returns>归属角色标识字符串</returns>
    public override string OnGettingSpellOwnerIdentifier()
    {
        return SpellHelper.ShinkiOwnerIdentifier;
    }

    /// <summary>
    /// 宣言演出即将播放时被原生流程调用一次。
    /// </summary>
    /// <param name="isPositiveSpell">本次宣言是否为红卡（true）/黑卡（false）</param>
    /// <returns>是否允许游戏自动播放符卡宣言演出</returns>
    public override bool ShouldCallSpellDeclarationAuto(bool isPositiveSpell)
    {
        SpellHelper.SetCutinShift(SpellHelper.ShinkiOwnerIdentifier);
        return true;
    }

    /// <summary>
    /// 红卡「魔神降临」效果入口：开启神绮传送门
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，提供角色与回调等信息</param>
    /// <returns>il2cpp 协程迭代器</returns>
    public override IEnumerator OnPositiveBuffExecute(SpellExecutionContext spellExecutionContext)
    {
        return PositiveBuffRoutine().WrapToIl2Cpp();
    }

    /// <summary>
    /// 红卡主协程：注册传送门常驻 Buff，并启动周期召唤魔界客人。
    /// </summary>
    /// <returns>托管协程迭代器</returns>
    [HideFromIl2Cpp]
    private System.Collections.IEnumerator PositiveBuffRoutine()
    {
        Log.LogInfo("[Shinki] 红卡【魔神降临】触发，开始注册传送门");
        RegisterPortalBuff();
        StartPortalSummoning();
        yield break;
    }

    /// <summary>
    /// 黑卡「绮符·环游魔界80天」效果入口：启动黑卡驱逐动画协程，驱赶魔界客人并送神绮离场。
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，提供触发角色控制器等信息</param>
    /// <returns>il2cpp 协程迭代器</returns>
    public override IEnumerator OnNegativeBuffExecute(SpellExecutionContext spellExecutionContext)
    {
        return NegativeBuffRoutine(spellExecutionContext).WrapToIl2Cpp();
    }

    /// <summary>
    /// 黑卡主协程：停召唤与传送门 Buff，播放神绮走位开门、客人走向传送门淡出、神绮淡出，末尾销毁传送门视觉。
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，含触发符卡的神绮控制器。</param>
    /// <returns>托管协程迭代器</returns>
    [HideFromIl2Cpp]
    private static System.Collections.IEnumerator NegativeBuffRoutine(SpellExecutionContext spellExecutionContext)
    {
        Log.LogInfo("[Shinki] 黑卡【绮符·环游魔界80天】触发，开始驱逐动画");

        SpellHelper.InterruptAllShinkiPortalBuffs();
        StopPortalSummoning();

        var shinkiController = ResolveShinkiController(spellExecutionContext);
        var affectedGuests = CollectAffectedGuests(shinkiController);

        if (shinkiController == null && affectedGuests.Count == 0)
        {
            Log.LogInfo("[Shinki] 黑卡：场上无神绮与魔界客人，跳过动画");
            DestroyPortalVisual();
            yield break;
        }

        var portalPosition = DeterminePortalPosition();
        var shinkiStandPos = portalPosition + BlackCardShinkiStandOffset;

        foreach (var ctrl in affectedGuests)
        {
            PartialCleanupForBlackCard(ctrl);
        }
        if (shinkiController != null)
        {
            PartialCleanupForBlackCard(shinkiController);
        }

        if (shinkiController != null)
        {
            shinkiController.MoveToTargetPosition(
                BlackCardMoveRotationDefault,
                new Il2CppSystem.Nullable<UnityEngine.Vector3>(shinkiStandPos),
                UnityEngine.Vector3Int.zero,
                false,
                null);
        }
        yield return new UnityEngine.WaitForSeconds(BlackCardWalkDuration);
        CreatePortalVisual();

        var leaveTracker = new BlackCardLeaveTracker(affectedGuests);
        foreach (var ctrl in affectedGuests)
        {
            if (ctrl == null) continue;
            ctrl.MoveToTargetPosition(
                BlackCardMoveRotationDefault,
                new Il2CppSystem.Nullable<UnityEngine.Vector3>(portalPosition),
                UnityEngine.Vector3Int.zero,
                false,
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<GuestGroupController>>(
                    new System.Action<GuestGroupController>(leaveTracker.OnGuestArrived)));
        }

        var guestLeaveElapsed = 0f;
        while (guestLeaveElapsed < BlackCardGuestLeaveTimeout)
        {
            foreach (var ctrl in leaveTracker.ConsumeArrived())
            {
                RepelGuestSafely(ctrl);
            }
            if (leaveTracker.Remaining == 0 && !leaveTracker.HasPending) break;
            yield return null;
            guestLeaveElapsed += UnityEngine.Time.deltaTime;
        }
        foreach (var ctrl in leaveTracker.ConsumeArrived())
        {
            RepelGuestSafely(ctrl);
        }
        if (leaveTracker.Remaining > 0)
        {
            Log.LogWarning($"[Shinki] 黑卡：{leaveTracker.Remaining} 名客人超时未离场，强制进入神绮离场阶段");
        }

        if (shinkiController != null)
        {
            RepelGuestSafely(shinkiController);
        }

        yield return new UnityEngine.WaitForSeconds(BlackCardPortalDisplayDuration);
        DestroyPortalVisual();
        Log.LogInfo("[Shinki] 黑卡驱逐完成，魔界客人已送返");
    }

    /// <summary>
    /// 注册神绮传送门常驻 Buff，并创建传送门视觉；描述回调传 null 由通用接管层以完好描述串写入。
    /// </summary>
    [HideFromIl2Cpp]
    private void RegisterPortalBuff()
    {
        PortalWorldPosition = DeterminePortalPosition();
        CreatePortalVisual();
        SpellHelper.RegisterConsistentBuff(
            Manager,
            (EventManager.BuffType)ShinkiPortalBuffType,
            null,
            null,
            out var onInterruptThisBuffCallback);
        SpellHelper.ShinkiPortalInterruptCallbacks.Add(onInterruptThisBuffCallback);
        SpellHelper.RegisterIzakayaCloseCleanup(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                new System.Action(OnIzakayaClosing)));
    }

    /// <summary>
    /// 打烊兜底入口：夜场结束时由 U11 打烊 Patch 调用，主动中断常驻传送门 Buff、取消周期召唤并销毁视觉。
    /// </summary>
    [HideFromIl2Cpp]
    internal static void OnIzakayaClosing()
    {
        CleanupPortalState();
        Log.LogInfo("[Shinki] 打烊兜底：已中断传送门常驻 Buff 并清理召唤与视觉");
    }

    /// <summary>
    /// 神绮传送门状态清理核心：中断全部常驻传送门 Buff、取消周期召唤定时器、销毁传送门视觉。
    /// 黑卡驱逐与打烊兜底共用，集中单一清理路径避免重复逻辑。
    /// </summary>
    [HideFromIl2Cpp]
    private static void CleanupPortalState()    {
        SpellHelper.InterruptAllShinkiPortalBuffs();
        StopPortalSummoning();
        DestroyPortalVisual();
    }

    /// <summary>
    /// 取触发符卡的神绮本人控制器：直接取上下文的触发控制器（单机与联机均有效，无需 GuestsMap 登记）。
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，含触发符卡的特殊客人控制器。</param>
    /// <returns>神绮控制器；上下文无控制器时返回 null。</returns>
    [HideFromIl2Cpp]
    private static GuestGroupController ResolveShinkiController(SpellExecutionContext spellExecutionContext)
    {
        return spellExecutionContext?.GuestsController;
    }

    /// <summary>
    /// 收集黑卡需驱逐的魔界客人：原生枚举在座与排队控制器，排除神绮本体。
    /// </summary>
    /// <param name="shinkiController">神绮控制器实例，用于从受影响的客人中剔除神绮本人；空则不做剔除。</param>
    /// <returns>受影响魔界客人控制器列表。</returns>
    [HideFromIl2Cpp]
    private static List<GuestGroupController> CollectAffectedGuests(GuestGroupController shinkiController)
    {
        var affected = new List<GuestGroupController>();
        var guestsManager = GuestsManager.Instance;
        if (guestsManager != null)
        {
            foreach (var ctrl in guestsManager.AllGuestInDeskController.ToArray())
            {
                if (ctrl == null || IsSameController(ctrl, shinkiController)) continue;
                affected.Add(ctrl);
            }
        }
        foreach (var ctrl in GuestGroupController.QueuedGuestControllers.ToArray())
        {
            if (ctrl == null || IsSameController(ctrl, shinkiController)) continue;
            affected.Add(ctrl);
        }
        return affected;
    }

    /// <summary>
    /// 判断两个客人控制器是否指向同一原生对象：IL2CPP 下每次访问会生成新托管包装，须比原生指针而非引用。
    /// </summary>
    /// <param name="a">待比较控制器甲。</param>
    /// <param name="b">待比较控制器乙。</param>
    /// <returns>指向同一原生对象返回 true。</returns>
    [HideFromIl2Cpp]
    private static bool IsSameController(GuestGroupController a, GuestGroupController b)
    {
        return a != null && b != null && a.Pointer == b.Pointer;
    }

    /// <summary>
    /// 黑卡客人离场追踪器：以命名方法作为原生到达回调
    /// 到达回调仅把客人原生指针登记入待离场集合，真正的离场由主协程在下一帧执行，
    /// 使离场动作与到达回调解耦，避免在同一调用栈内重入移动完成回调。
    /// </summary>
    private class BlackCardLeaveTracker
    {
        internal int Remaining { get; private set; }

        private readonly HashSet<long> _arrivedPointers = new();

        private readonly Dictionary<long, GuestGroupController> _pointerToController = new();

        /// <summary>
        /// 构造追踪器：登记所有受影响客人的指针与控制器映射，初始化剩余计数。
        /// </summary>
        /// <param name="affectedGuests">受影响客人控制器列表（已过滤 null）。</param>
        internal BlackCardLeaveTracker(List<GuestGroupController> affectedGuests)
        {
            Remaining = affectedGuests.Count;
            foreach (var ctrl in affectedGuests)
            {
                if (ctrl == null) continue;
                _pointerToController[ctrl.Pointer.ToInt64()] = ctrl;
            }
        }

        /// <summary>
        /// 原生移动到达回调：客人走到传送门后将其原生指针登记入待离场集合（不直接离场）。
        /// </summary>
        /// <param name="arrivedController">到达传送门的客人控制器（原生回调传入）。</param>
        internal void OnGuestArrived(GuestGroupController arrivedController)
        {
            if (arrivedController == null) return;
            var pointer = arrivedController.Pointer.ToInt64();
            if (_arrivedPointers.Add(pointer) && Remaining > 0)
            {
                Remaining--;
            }
        }

        /// <summary>
        /// 取出并清空当前已登记的待离场客人控制器列表。
        /// </summary>
        /// <returns>已登记待离场的客人控制器列表。</returns>
        internal List<GuestGroupController> ConsumeArrived()
        {
            var arrived = new List<GuestGroupController>();
            foreach (var pointer in _arrivedPointers)
            {
                if (_pointerToController.TryGetValue(pointer, out var ctrl) && ctrl != null)
                {
                    arrived.Add(ctrl);
                }
            }
            _arrivedPointers.Clear();
            return arrived;
        }

        /// <summary>
        /// 是否仍有已登记但主协程尚未消费的待离场客人（用于主协程提前退出判定）。
        /// </summary>
        internal bool HasPending => _arrivedPointers.Count > 0;
    }

    /// <summary>
    /// 黑卡阶段清理：清理客人的订单面板与排队登记、移除耐心倒计时，但保留桌位待淡出时释放。
    /// </summary>
    /// <param name="controller">目标客人控制器；空则跳过。</param>
    [HideFromIl2Cpp]
    private static void PartialCleanupForBlackCard(GuestGroupController controller)
    {
        if (controller == null) return;
        if (controller.DeskCode != -1)
        {
            GuestsManager.Instance.RemoveFromPatientCountdown(controller);
            GuestFSM.TryCloseServePanel(controller.DeskCode);
        }
        else if (controller.IsQueued(out _))
        {
            controller.RemoveFromQueue();
            GuestsManager.Instance.RemoveFromPatientCountdown(controller);
        }
    }

    /// <summary>
    /// 黑卡安全离场：按客人是否已入座选择原生离场 API。已入座客人（DeskCode 有效）用 LeaveFromDesk(Fading) 淡出；
    /// </summary>
    /// <param name="controller">目标客人控制器；空则跳过。</param>
    [HideFromIl2Cpp]
    private static void RepelGuestSafely(GuestGroupController controller)
    {
        if (controller == null) return;
        if (controller.DeskCode != -1)
        {
            GuestsManager.Instance.LeaveFromDesk(controller, GuestGroupController.LeaveType.Fading, null, false);
        }
        else
        {
            GuestsManager.Instance.FlyCharaRepell(controller);
        }
    }

    /// <summary>
    /// 计算 U9 客人召唤用的世界坐标锚点：屏幕比例反算世界坐标；仅供客人 overrideSpawnPosition，与传送门视觉无关。
    /// </summary>
    /// <returns>客人召唤世界坐标。</returns>
    [HideFromIl2Cpp]
    private static UnityEngine.Vector3 DeterminePortalPosition()
    {
        var camera = UnityEngine.Camera.main;
        if (camera == null)
        {
            Log.LogWarning("[Shinki] Camera.main 未就绪，客人召唤回退默认坐标");
            return PortalFallbackPosition;
        }
        var screenX = UnityEngine.Screen.width * PortalScreenXRatio;
        var screenY = UnityEngine.Screen.height * PortalScreenYRatio;
        var worldPoint = camera.ScreenToWorldPoint(
            new UnityEngine.Vector3(screenX, screenY, camera.nearClipPlane));
        return new UnityEngine.Vector3(
            worldPoint.x + PortalWorldOffsetX, worldPoint.y, PortalGroundZ);
    }

    /// <summary>
    /// 在屏幕空间 UI 层创建传送门视觉：ScreenSpaceOverlay Canvas 承载 Image，按固定屏幕锚点比例定位（不依赖相机/世界坐标，位置恒定）。
    /// </summary>
    [HideFromIl2Cpp]
    private static void CreatePortalVisual()
    {
        DestroyPortalVisual();
        if (!ResourceExManager.TryGetSprite(PortalVisualUri, out var portalSprite) || portalSprite == null)
        {
            Log.LogWarning($"[Shinki] 加载传送门贴图失败：{PortalVisualUri}");
            return;
        }

        var canvasObject = new UnityEngine.GameObject("Shinki_MakaiPortal");
        var canvas = canvasObject.AddComponent<UnityEngine.Canvas>();
        canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = PortalCanvasSortingOrder;

        var portalObject = new UnityEngine.GameObject("Shinki_PortalImage");
        portalObject.transform.SetParent(canvasObject.transform, false);

        var image = portalObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = portalSprite;
        image.color = PortalInitialColor;
        image.preserveAspect = true;

        var rect = image.rectTransform;
        // 固定屏幕锚点：以 PortalScreenX/YRatio 为中心（叠加向右偏移 PortalScreenXOffsetRatio），PortalAnchorHalfSize 为半边长，
        // 位置恒定不随相机/世界坐标变化，保证传送门始终显示在屏幕同一处。
        var centerX = PortalScreenXRatio + PortalScreenXOffsetRatio;
        rect.anchorMin = new UnityEngine.Vector2(centerX - PortalAnchorHalfSize, PortalScreenYRatio - PortalAnchorHalfSize);
        rect.anchorMax = new UnityEngine.Vector2(centerX + PortalAnchorHalfSize, PortalScreenYRatio + PortalAnchorHalfSize);
        rect.pivot = PortalCenterPivot;
        rect.sizeDelta = UnityEngine.Vector2.zero;
        rect.anchoredPosition = UnityEngine.Vector2.zero;

        _portalVisual = canvasObject;
        Log.LogInfo($"[Shinki] 传送门视觉已创建（固定屏幕锚点 {centerX},{PortalScreenYRatio}）：{PortalVisualUri}");
    }

    /// <summary>
    /// 销毁传送门视觉 Canvas，并清空句柄。
    /// </summary>
    [HideFromIl2Cpp]
    private static void DestroyPortalVisual()
    {
        if (_portalVisual != null)
        {
            UnityEngine.Object.Destroy(_portalVisual);
            _portalVisual = null;
        }
    }

    /// <summary>
    /// 启动周期召唤：幂等标记激活后，立即召唤首批并注册定时器，每隔固定间隔从传送门召唤新客人。
    /// </summary>
    [HideFromIl2Cpp]
    private static void StartPortalSummoning()
    {
        if (_portalSummoningActive)
        {
            Log.LogInfo("[Shinki] 周期召唤已激活，跳过重复启动");
            return;
        }
        _portalSummoningActive = true;
        SummonRandomMakaiGuests(PortalSummonBatchCount);
        CommandScheduler.EnqueueInterval(
            PortalSummonTimerId, PortalSummonIntervalSeconds, OnPortalTick);
        Log.LogInfo($"[Shinki] 周期召唤已启动：间隔 {PortalSummonIntervalSeconds}s，每批 {PortalSummonBatchCount} 人");
    }

    /// <summary>
    /// 停止周期召唤：取消定时器并复位激活标记，供黑卡/打烊兜底调用。
    /// </summary>
    [HideFromIl2Cpp]
    private static void StopPortalSummoning()
    {
        CommandScheduler.CancelInterval(PortalSummonTimerId);
        _portalSummoningActive = false;
    }

    /// <summary>
    /// 周期召唤定时回调：检查夜场剩余时间，超时则停止召唤，否则召唤一批魔界客人。
    /// </summary>
    [HideFromIl2Cpp]
    private static void OnPortalTick()
    {
        if (EventManager.Instance == null)
        {
            StopPortalSummoning();
            return;
        }
        var remaining = EventManager.Instance.TotalCountDown + EventManager.Instance.extraCountDown;
        if (remaining <= 0f)
        {
            Log.LogInfo("[Shinki] 夜场时间耗尽，停止传送门召唤");
            StopPortalSummoning();
            return;
        }
        SummonRandomMakaiGuests(PortalSummonBatchCount);
    }

    /// <summary>
    /// 从魔界客人池随机召唤指定数量的客人：按概率在稀客与普客间选择，稀客做在场去重。
    /// </summary>
    /// <param name="count">本次召唤的客人数量。</param>
    [HideFromIl2Cpp]
    private static void SummonRandomMakaiGuests(int count)
    {
        if (GuestsManager.Instance == null)
        {
            Log.LogWarning("[Shinki] GuestsManager 未就绪，跳过召唤");
            return;
        }

        var onFieldSpecialIds = SpellHelper.GetOnFieldSpecialGuestIds();
        var availableSpecial = FilterAvailableSpecial(onFieldSpecialIds);

        for (var i = 0; i < count; i++)
        {
            var summonSpecial = availableSpecial.Count > 0 &&
                (MakaiNormalGuestIds.Count == 0 || UnityEngine.Random.value < PortalSummonSpecialRatio);
            if (summonSpecial)
            {
                var id = availableSpecial[UnityEngine.Random.Range(0, availableSpecial.Count)];
                if (TrySummonSpecialMakaiGuest(id))
                {
                    availableSpecial.Remove(id);
                }
            }
            else
            {
                SummonRandomNormalMakaiGuest();
            }
        }
    }

    /// <summary>
    /// 从魔界稀客池筛出当前可召唤（未在场、数据存在、可用）的稀客 id 列表。
    /// </summary>
    /// <param name="onFieldSpecialIds">当前在场稀客 id 集合，用于排除已出现的稀客。</param>
    /// <returns>可召唤的魔界稀客 id 列表。</returns>
    [HideFromIl2Cpp]
    private static List<int> FilterAvailableSpecial(HashSet<int> onFieldSpecialIds)
    {
        var available = new List<int>();
        foreach (var id in MakaiSpecialGuestIds)
        {
            if (onFieldSpecialIds.Contains(id)) continue;
            if (DataBaseCharacter.RefSGuest(id) == null) continue;
            if (!PlayerManager.SpecialGuestAvailable(id)) continue;
            available.Add(id);
        }
        return available;
    }

    /// <summary>
    /// 召唤单个魔界稀客入场：从传送门世界坐标出现，并标记今晚已生成以防自然刷客重复。
    /// </summary>
    /// <param name="guestId">目标稀客 id。</param>
    /// <returns>召唤成功返回 true；数据缺失或不可用返回 false。</returns>
    [HideFromIl2Cpp]
    private static bool TrySummonSpecialMakaiGuest(int guestId)
    {
        if (DataBaseCharacter.RefSGuest(guestId) == null)
        {
            Log.LogWarning($"[Shinki] 稀客数据不存在，跳过 id={guestId}");
            return false;
        }
        if (!PlayerManager.SpecialGuestAvailable(guestId))
        {
            Log.LogWarning($"[Shinki] 稀客 id={guestId} 当前不可用，跳过");
            return false;
        }
        GuestsManager.Instance.SpawnSpecialGuestGroup(
            guestId,
            SpecialGuestsController.GuestSpawnType.Normal,
            new Il2CppSystem.Nullable<UnityEngine.Vector3>(PortalWorldPosition),
            null,
            GuestGroupController.LeaveType.Move,
            true,
            PortalSummonUnspecifiedDeskCode,
            false,
            null,
            true);
        EventManager.Instance.SetTargetGuestHasSpawnedHandle?.Invoke(guestId);
        Log.LogInfo($"[Shinki] 召唤魔界稀客 id={guestId} 于传送门 {PortalWorldPosition}");
        return true;
    }

    /// <summary>
    /// 从魔界普客池随机召唤一名普客：校验数据存在与可用性后从传送门世界坐标出现。
    /// </summary>
    [HideFromIl2Cpp]
    private static void SummonRandomNormalMakaiGuest()
    {
        if (MakaiNormalGuestIds.Count == 0) return;
        var id = MakaiNormalGuestIds[UnityEngine.Random.Range(0, MakaiNormalGuestIds.Count)];
        if (DataBaseCharacter.RefNGuest(id) == null)
        {
            Log.LogWarning($"[Shinki] 普客数据不存在，跳过 id={id}");
            return;
        }
        if (!PlayerManager.NormalGuestAvailable(id))
        {
            Log.LogWarning($"[Shinki] 普客 id={id} 当前不可用，跳过");
            return;
        }
        var normalGuest = DataBaseCharacter.RefNGuest(id);
        var guestList = new Il2CppSystem.Collections.Generic.List<NormalGuest>();
        guestList.Add(normalGuest);
        GuestsManager.Instance.SpawnNormalGuestGroup(
            guestList.ToIEnumerable(),
            new Il2CppSystem.Nullable<UnityEngine.Vector3>(PortalWorldPosition));
        Log.LogInfo($"[Shinki] 召唤魔界普客 id={id} 于传送门 {PortalWorldPosition}");
    }
}
