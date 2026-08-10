using System;
using System.Collections.Generic;
using HarmonyLib;

using GameData.CoreLanguage.Collections;
using MetaMystia.UI;
using NightScene.EventUtility;
using NightScene.GuestManagementUtility;

namespace MetaMystia.ResourceEx.SpellCollection.Koakuma;

/// <summary>
/// 小恶魔红卡「灵符·遗失典籍的回响（Echo）」的订单 tag 揭示
/// </summary>
public static partial class KoakumaOrderRevealEffect
{
    // 传菜界面揭示文本颜色（蓝色，与 poc 一致）。
    internal const string RevealTagColor = "#42A5F5";

    // DeskCode → (料理tag名, 饮品tag名) 缓存
    internal static readonly Dictionary<int, (string FoodTag, string BevTag)> TagCache = new();
}

/// <summary>
/// 订单生成拦截：稀客点单成功且 Echo 生效时，读取 food/beverage tag 缓存并按桌号记录，再消耗一次 Echo 计数。
/// </summary>
[HarmonyPatch(typeof(GuestsManager.__c__DisplayClass174_0),
    nameof(GuestsManager.__c__DisplayClass174_0.Method_Internal_OrderGenerationResult_GuestGroupController_byref_OrderBase_0))]
[AutoLog]
public static partial class KoakumaOrderRevealCapture
{
    /// <summary>
    /// 稀客订单生成后捕获其 tag 并消耗一次 Echo 计数（Postfix）。
    /// </summary>
    /// <param name="__result">订单生成结果（Succeed 才揭示）。</param>
    /// <param name="toGenerate">被生成的客群控制器，用于判断是否为稀客。</param>
    /// <param name="orderData">订单数据，持有 foodRequest/beverageRequest tag id 与 DeskCode。</param>
    [HarmonyPostfix]
    public static void GenerateOrderInternal_Postfix(
        GuestsManager.OrderGenerationResult __result,
        GuestGroupController toGenerate,
        ref GuestsManager.OrderBase orderData)
    {
        var eventManager = EventManager.Instance;
        if (eventManager == null) return;
        if (!eventManager.CheckCountedBuffExists((EventManager.BuffType)Spell_Koakuma.KoakumaEchoBuffType)) return;
        if (__result != GuestsManager.OrderGenerationResult.Succeed) return;
        if (toGenerate.ControllType != GuestsManager.GuestType.Special) return;

        try
        {
            var foodTagId = orderData.foodRequest;
            var bevTagId = orderData.beverageRequest;

            var foodTagName = ResolveTagName(DataBaseLanguage.FoodTags, foodTagId);
            var bevTagName = ResolveTagName(DataBaseLanguage.BeverageTags, bevTagId);

            if (foodTagName == null && bevTagName == null)
            {
                Log.LogWarning("[KoakumaOrderReveal] 红卡：订单无有效 tag，跳过揭示");
                return;
            }

            var deskCode = orderData.DeskCode;
            KoakumaOrderRevealEffect.TagCache[deskCode] = (foodTagName, bevTagName);
            Log.LogInfo($"[KoakumaOrderReveal] 红卡：缓存 tag DeskCode={deskCode} food=\"{foodTagName}\" bev=\"{bevTagName}\"");

            eventManager.TryDeductCountedBuffValue((EventManager.BuffType)Spell_Koakuma.KoakumaEchoBuffType, true);
        }
        catch (Exception ex)
        {
            Log.LogError($"[KoakumaOrderReveal] 红卡揭示异常: {ex}");
        }
    }

    /// <summary>
    /// 将 tag id 解析为本地化 tag 名。
    /// </summary>
    /// <param name="tagTable">料理/饮品 tag 字典（DataBaseLanguage.FoodTags / BeverageTags）。</param>
    /// <param name="tagId">tag 标识，0 表示无。</param>
    /// <returns>tag 名；无效时返回 null。</returns>
    private static string ResolveTagName(Il2CppSystem.Collections.Generic.Dictionary<int, string> tagTable, int tagId)
    {
        if (tagId == 0 || tagTable == null) return null;
        if (tagTable.TryGetValue(tagId, out var name)) return name;
        return $"#{tagId}";
    }
}

/// <summary>
/// 传菜界面文本覆写：按桌号取出已缓存的 tag，于 GetOrderBevText 返回文本后追加蓝色揭示信息。
/// </summary>
[HarmonyPatch(typeof(SpecialGuestsController), "GetOrderBevText")]
[AutoLog]
public static partial class KoakumaOrderRevealText
{
    /// <summary>
    /// 传菜界面文本后追加红卡揭示的 tag 信息（Postfix）。
    /// </summary>
    /// <param name="__result">原生传菜文本，原地追加揭示信息。</param>
    /// <param name="specialOrder">稀客订单，提供 DeskCode 以匹配缓存。</param>
    [HarmonyPostfix]
    public static void GetOrderBevText_Postfix(ref string __result, GuestsManager.SpecialOrder specialOrder)
    {
        if (specialOrder == null) return;

        var deskCode = specialOrder.DeskCode;
        if (!KoakumaOrderRevealEffect.TagCache.TryGetValue(deskCode, out var tags)) return;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(tags.FoodTag)) parts.Add($"料理tag：{tags.FoodTag}");
        if (!string.IsNullOrEmpty(tags.BevTag)) parts.Add($"饮品tag：{tags.BevTag}");
        if (parts.Count == 0) return;

        var tagInfo = string.Join("，", parts);
        var prefix = TextId.Spell_Koakuma_OrderRevealPrefix.Get(tagInfo);
        __result = $"{__result}\n<color={KoakumaOrderRevealEffect.RevealTagColor}>{prefix}</color>";
        Log.LogInfo($"[KoakumaOrderReveal] 红卡：传菜界面附加 tag \"{tagInfo}\" (DeskCode={deskCode})");
    }
}
