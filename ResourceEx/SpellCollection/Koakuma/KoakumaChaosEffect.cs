using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;
using Il2CppSystem;
using SgrYuki;

using MetaMystia.UI;
using Common.UI;
using NightScene.CookingUtility;
using NightScene.UI;
using NightScene.UI.CookingUtility;

namespace MetaMystia.ResourceEx.SpellCollection.Koakuma;

/// <summary>
/// 小恶魔黑卡「幻符·献给巴瓦鲁的镇魂曲」的混沌玩法：厨具随机化、食材分类列洗牌、混沌态开关与通知。
/// 玩法逻辑内聚于本文件；Harmony 织入拆为两个独立 [HarmonyPatch] 类（厨具 Prefix、食材 Postfix 各一），
/// 因 Harmony 在一个类上混用 class 级与方法级 [HarmonyPatch] 时只认其一，故不合并。不另设 Patches 转发层；
/// 混沌态由 Spell_Koakuma 经 Activate/Deactivate 切换。
/// </summary>
[AutoLog]
[HarmonyPatch(typeof(WorkSceneSustainedPannel), nameof(WorkSceneSustainedPannel.OpenCookingSelectionPannel))]
public static partial class KoakumaChaosEffect
{
    // 候选厨具收集容量：场上厨具数量有限，预分配避免每帧扩容。
    private const int CandidateCookerCapacity = 8;

    // 厨具随机用的随机源（与主线程烹饪逻辑隔离，避免与食材洗牌共用同一实例产生耦合）。
    private static readonly System.Random CookwareRandom = new();
    // 反射失败成员名缓存：每个成员仅告警一次，避免每帧刷屏。
    private static readonly HashSet<string> ReflectionFailCache = new();

    // 黑卡混沌是否处于激活态：上限单一 bool（原版符卡一般不叠加），由 Spell_Koakuma 在激活/到期时切换。
    private static bool _chaosActive;

    /// <summary>
    /// 黑卡混沌激活态的公开只读访问：厨具随机化/食材打乱织入据此判断是否介入面板。
    /// </summary>
    /// <returns>混沌激活返回 true，否则 false。</returns>
    internal static bool IsChaosActive => _chaosActive;

    /// <summary>
    /// 激活黑卡混沌态：置 _chaosActive=true，使厨具/食材织入开始介入料理面板。
    /// </summary>
    /// <returns>无。</returns>
    internal static void Activate()
    {
        _chaosActive = true;
        Log.LogInfo("[KoakumaChaosEffect] 黑卡混沌激活：厨具与食材开始乱套");
    }

    /// <summary>
    /// 混沌到期复位：置 _chaosActive=false，使厨具/食材织入停止介入料理面板。
    /// </summary>
    /// <returns>无。</returns>
    internal static void Deactivate()
    {
        _chaosActive = false;
        Log.LogInfo("[KoakumaChaosEffect] 黑卡混沌结束：厨具与食材恢复正常");
    }

    /// <summary>
    /// 黑卡激活时弹出恶作剧通知，提示玩家厨具与食材已乱套。
    /// </summary>
    /// <returns>无。</returns>
    internal static void NotifyChaosStart()
    {
        if (ReceivedObjectDisplayerController.Instance == null) return;
        ReceivedObjectDisplayerController.Instance.NotifyTextMessage(
            TextId.Spell_Koakuma_ChaosNotify.Get());
    }

    /// <summary>
    /// 料理面板打开入口处介入：当混沌激活时，将目标厨具替换为随机空闲厨具；否则放行原生。
    /// </summary>
    /// <param name="cookController">待打开的厨具（ref，可被替换为随机厨具）。</param>
    /// <param name="setIngredientFieldAlpha">食材栏透明度，原样透传。</param>
    /// <param name="setRecipeFieldAlpha">配方栏透明度，原样透传。</param>
    /// <returns>恒 true（仅替换参数，不拦截原生流程）。</returns>
    [HarmonyPrefix]
    public static bool OpenCookingSelectionPannel_Prefix(
        ref CookController cookController, float setIngredientFieldAlpha, float setRecipeFieldAlpha)
    {
        if (!_chaosActive) return true;

        var target = FindRandomIdleController(cookController);
        if (target == null) return true;

        Log.LogInfo($"[KoakumaChaosEffect] 厨具随机化：GridIndex={cookController.GridIndex} → GridIndex={target.GridIndex}");
        cookController = target;
        return true;
    }

    /// <summary>
    /// 从全部厨具中挑一个随机空闲厨具：排除当前厨具与空桌，优先空闲态，无空闲则从全部候选随机。
    /// </summary>
    /// <param name="exclude">当前正要打开的厨具，须排除（避免"随机到自身"无意义替换）。</param>
    /// <returns>随机选中的空闲厨具；无候选时返回 null（调用方据此放行原生）。</returns>
    private static CookController FindRandomIdleController(CookController exclude)
    {
        var cookSystem = CookSystemManager.Instance;
        if (cookSystem == null) return null;

        var allCookers = cookSystem.AllCookers;
        if (allCookers == null) return null;

        var candidates = new List<CookController>(CandidateCookerCapacity);
        var idleCandidates = new List<CookController>(CandidateCookerCapacity);
        foreach (var kvp in allCookers)
        {
            var controller = kvp.Value;
            if (controller == null) continue;
            if (controller.Pointer == exclude.Pointer) continue;
            if (controller.IsEmptyDesk) continue;

            candidates.Add(controller);
            if (controller.Phase == CookController.CookPhase.Idle)
            {
                idleCandidates.Add(controller);
            }
        }

        if (candidates.Count == 0) return null;

        var pool = idleCandidates.Count > 0 ? idleCandidates : candidates;
        return pool[CookwareRandom.Next(pool.Count)];
    }
}

/// <summary>
/// 小恶魔黑卡食材顺序打乱的织入类
/// </summary>
[AutoLog]
[HarmonyPatch(typeof(WorkSceneCookingSelectionPannel), "UpdateAllVisual")]
public static partial class KoakumaChaosIngredientShuffle
{
    // 4 个食材分类 list 的字段名（原生真实拼写 Insatance，非笔误），顺序对应列 0~3。
    private static readonly string[] IngredientFieldNames =
    {
        "m_Ingredient_SeaFoodInstances",
        "m_Ingredient_MeatInstances",
        "m_Ingredient_VeggiesInsatance",
        "m_Ingredient_OtherInstances",
    };

    // 食材栏 UI 分组字段名：重排引用后需强制其 UpdateElements 重画。
    private const string StaticIngredientsGroupField = "m_StaticIngredientsGroup";
    // 食材栏分组重画方法名（反射调用，结果缓存于 UpdateElementsMethodCache）。
    private const string UpdateElementsMethod = "UpdateElements";
    // 食材栏分组 UpdateElements 的 MethodInfo 缓存：类型固定，反射取一次后复用（规则 24 反射须缓存）。
    private static System.Reflection.MethodInfo _updateElementsMethod;

    // 食材洗牌专用随机源（与厨具随机化隔离，避免共用随机实例耦合）。
    private static readonly System.Random ShuffleRandom = new();
    // 反射失败成员名缓存：每个成员仅告警一次，避免每帧刷屏。
    private static readonly HashSet<string> ReflectionFailCache = new();

    /// <summary>
    /// Postfix：混沌激活时，对 4 个食材分类 list 引用做 Fisher-Yates 洗牌，再重刷 UI。
    /// </summary>
    /// <param name="__instance">料理面板实例，持有 4 个食材分类 list 与 UI 分组。</param>
    [HarmonyPostfix]
    public static void UpdateAllVisual_Postfix(WorkSceneCookingSelectionPannel __instance)
    {
        if (!KoakumaChaosEffect.IsChaosActive) return;

        try
        {
            ReorderIngredientContents(__instance);
        }
        catch (System.Exception ex)
        {
            Log.LogError($"[KoakumaChaosIngredientShuffle] 食材栏重排异常（非致命）: {ex}");
        }
    }

    /// <summary>
    /// 取出 4 个食材分类 list 引用，Fisher-Yates 洗牌其绑定顺序写回 panel 字段，再强制 UI 分组刷新。
    /// </summary>
    /// <param name="panel">料理面板实例。</param>
    private static void ReorderIngredientContents(WorkSceneCookingSelectionPannel panel)
    {
        var lists = new object[IngredientFieldNames.Length];
        for (var k = 0; k < IngredientFieldNames.Length; k++)
        {
            lists[k] = GetPrivateMemberValue(panel, IngredientFieldNames[k]);
            if (lists[k] == null)
            {
                Log.LogWarning("[KoakumaChaosIngredientShuffle] 食材栏重排跳过：未取到全部 4 个分类 list，放弃本次洗牌");
                return;
            }
        }

        for (var i = lists.Length - 1; i > 0; i--)
        {
            var j = ShuffleRandom.Next(i + 1);
            (lists[i], lists[j]) = (lists[j], lists[i]);
        }

        for (var k = 0; k < IngredientFieldNames.Length; k++)
        {
            SetPrivateMemberValue(panel, IngredientFieldNames[k], lists[k]);
        }

        Log.LogInfo("[KoakumaChaosIngredientShuffle] 食材栏列引用洗牌完成");
        RefreshIngredientsGroup(panel);
    }

    /// <summary>
    /// 反射调用 m_StaticIngredientsGroup.UpdateElements() 重刷食材栏渲染。
    /// </summary>
    /// <param name="panel">料理面板实例。</param>
    private static void RefreshIngredientsGroup(WorkSceneCookingSelectionPannel panel)
    {
        var group = GetPrivateMemberValue(panel, StaticIngredientsGroupField);
        if (group == null) return;

        if (_updateElementsMethod == null)
        {
            _updateElementsMethod = group.GetType().GetMethod(
                UpdateElementsMethod, BindingFlags.Public | BindingFlags.Instance);
        }
        _updateElementsMethod?.Invoke(group, null);
    }

    /// <summary>
    /// 反射取料理面板的私有属性值（il2cpp 私有字段以属性形式暴露）。
    /// </summary>
    /// <param name="obj">目标实例。</param>
    /// <param name="memberName">真实私有成员名（含 m_ 前缀）。</param>
    /// <returns>成员值；未找到返回 null。</returns>
    private static object GetPrivateMemberValue(object obj, string memberName)
    {
        var type = obj.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        var prop = type.GetProperty(memberName, flags);
        if (prop != null) return prop.GetValue(obj);

        if (ReflectionFailCache.Add($"{type.Name}.{memberName}"))
            Log.LogWarning($"[KoakumaChaosIngredientShuffle] 反射: 未找到属性 {type.Name}.{memberName}");
        return null;
    }

    /// <summary>
    /// 反射写料理面板的私有属性值（il2cpp 私有字段以属性形式暴露）。
    /// </summary>
    /// <param name="obj">目标实例。</param>
    /// <param name="memberName">真实私有成员名（含 m_ 前缀）。</param>
    /// <param name="value">待写入值。</param>
    private static void SetPrivateMemberValue(object obj, string memberName, object value)
    {
        var type = obj.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        var prop = type.GetProperty(memberName, flags);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
            return;
        }

        if (ReflectionFailCache.Add($"{type.Name}.{memberName}"))
            Log.LogWarning($"[KoakumaChaosIngredientShuffle] 反射: 未找到可写属性 {type.Name}.{memberName}");
    }
}
