using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Attributes;
using Il2CppSystem.Collections;
using System.Collections.Generic;
using GameData.Core.Collections.CharacterUtility;
using GameData.Profile;
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

    // 传送门视觉贴图路径：当前复用 Buff 图标素材。
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
    /// 黑卡「绮符·环游魔界80天」效果入口：移除红卡注册的传送门 Buff 并驱逐客人。
    /// 黑卡本身不注册 Buff；传送门 Buff 由本方法主动中断。
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，提供角色与回调等信息</param>
    /// <returns>il2cpp 协程迭代器</returns>
    public override IEnumerator OnNegativeBuffExecute(SpellExecutionContext spellExecutionContext)
    {
        RemovePortalBuff();
        return null;
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
    }

    /// <summary>
    /// 移除神绮传送门常驻 Buff：主动中断红卡注册的全部传送门 Buff、取消周期召唤定时器，并销毁传送门视觉。
    /// </summary>
    [HideFromIl2Cpp]
    private void RemovePortalBuff()
    {
        SpellHelper.InterruptAllShinkiPortalBuffs();
        StopPortalSummoning();
        DestroyPortalVisual();
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
