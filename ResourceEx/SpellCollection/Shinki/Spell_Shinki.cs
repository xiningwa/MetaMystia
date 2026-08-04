using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Attributes;
using Il2CppSystem.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using MetaMystia;
using MetaMystia.ResourceEx.SpellCollection;
using NightScene.EventUtility;

namespace MetaMystia.ResourceEx.SpellCollection.Shinki;

/// <summary>
/// 神绮符卡主类：红卡「魔神降临」召唤魔界客人，黑卡「绮符·环游魔界80天」。
/// </summary>
[AutoLog]
public partial class Spell_Shinki : SpellBase
{
    internal const int ShinkiPortalBuffType = 9002;

    // 传送门视觉贴图路径：先用 Buff 图标素材确保可见，后续可换为专用传送门贴图。
    private const string PortalVisualUri = "rex://ResourceExample/assets/Buff/9004_1.png";
    // 传送门屏幕水平锚点比例（0~1）：对齐 poc 固定设计，屏幕中央（不依赖相机/世界坐标）。
    private const float PortalScreenXRatio = 0.50f;
    // 传送门屏幕垂直锚点比例（0~1）：对齐 poc 固定设计，屏幕偏下区域。
    private const float PortalScreenYRatio = 0.25f;
    // 传送门向右偏移的屏幕比例：0.7 个身位（单身位约占屏幕宽 0.125 的估算），使门体避让入口右侧。
    private const float PortalScreenXOffsetRatio = 0.0875f;
    // 传送门屏幕锚点半尺寸比例（矩形半边长，0~0.5）：决定 UI 层门体大小（克制尺寸，避免遮挡画面）。
    private const float PortalAnchorHalfSize = 0.14f;
    // 屏幕空间覆盖层排序顺序：位于游戏 UI 之下、世界之上，确保传送门恒可见且不遮挡 Buff 栏。
    private const int PortalCanvasSortingOrder = 5;
    // U9 客人召唤用的世界坐标 X 右移偏移（poc 同值），与视觉无关。
    private const float PortalWorldOffsetX = 1.5f;
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
    /// 红卡主协程：注册传送门常驻 Buff
    /// </summary>
    /// <returns>托管协程迭代器</returns>
    [HideFromIl2Cpp]
    private System.Collections.IEnumerator PositiveBuffRoutine()
    {
        Log.LogInfo("[Shinki] 红卡【魔神降临】触发，开始注册传送门");
        RegisterPortalBuff();
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
    /// 注册神绮传送门常驻 Buff，并创建传送门世界视觉；描述回调传 null，由通用接管处用完好描述串兜底写入。
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
    /// 移除神绮传送门常驻 Buff：主动中断红卡注册的全部传送门 Buff，并销毁传送门视觉。
    /// </summary>
    [HideFromIl2Cpp]
    private void RemovePortalBuff()
    {
        SpellHelper.InterruptAllShinkiPortalBuffs();
        DestroyPortalVisual();
    }

    /// <summary>
    /// 计算 U9 客人召唤用的世界坐标锚点：屏幕比例反算世界坐标（poc 固定设计）；仅供客人 overrideSpawnPosition，与传送门视觉无关。
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
            worldPoint.x + PortalWorldOffsetX, worldPoint.y, worldPoint.z);
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
        // 位置恒定不随相机/世界坐标变化（poc 固定设计），保证传送门始终显示在屏幕同一处。
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
}
