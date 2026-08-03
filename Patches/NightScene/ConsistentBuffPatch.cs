using HarmonyLib;
using Il2CppSystem;

using GameData.CoreLanguage.Collections;
using NightScene.EventUtility;
using NightScene.UI;
using NightScene.UI.EventUtility;

namespace MetaMystia.Patches.NightScene;

/// <summary>
/// 接管全部 CONSISTENT（常驻）Buff 注册：对原生描述回调正常调用以保留 $a/$b 替换，对 Mod 的 null 回调改用完好描述串兜底；
/// 因无 Postfix 等价手段，须 Prefix 跳过原生控制流（规则 45 例外）。
/// </summary>
[HarmonyPatch(typeof(UIManager), "RegisterConsistentBuffRecord")]
internal static class ConsistentBuffPatch
{
    // CONSISTENT 为常驻无倒计时，进度遮罩恒空（fillAmount=0）使图标完整常亮；1f 经 BuffElement.UpdateBuff 的 1-progress 换算得出。
    private const float ConsistentBuffProgressFull = 1f;

    /// <summary>
    /// 拦截全部 CONSISTENT Buff 注册：重放原生加载与描述写入，保留原生回调的 $a/$b 替换，仅对 Mod null 回调改用完好串兜底。
    /// </summary>
    /// <param name="__instance">UIManager 实例，提供 buffModule 字段。</param>
    /// <param name="buffType">Buff 类型枚举，用于取 RefBuffLang 描述与名称。</param>
    /// <param name="guid">原生内部生成的 Buff 唯一标识，须原样透传给 BuffModule.RegisterBuff 以保证中断回调可定位。</param>
    /// <param name="getBuffDescriptionCallback">描述回调；非 null 为原生 CONSISTENT（含 $a 替换），null 为 Mod 路径用完好串兜底。</param>
    /// <param name="onBuffFinish">输出 Buff 结束回调，由 BuffModule.RegisterBuff 生成并写回。</param>
    /// <param name="titleOverride">标题覆写回调；非 null 时覆写 buffLang.Name。</param>
    /// <returns>恒为 false：跳过原生方法体（已自行完成加载与描述写入）。</returns>
    [HarmonyPrefix]
    public static bool RegisterConsistentBuffRecord_Prefix(
        UIManager __instance,
        EventManager.BuffType buffType,
        Il2CppSystem.Guid guid,
        Il2CppSystem.Func<string, string> getBuffDescriptionCallback,
        out Il2CppSystem.Action onBuffFinish,
        Il2CppSystem.Func<string, string> titleOverride)
    {
        var buffLang = DataBaseLanguage.RefBuffLang(buffType);
        var buffTitle = titleOverride != null
            ? titleOverride.Invoke(buffLang.Name)
            : buffLang.Name;
        var buffDescription = getBuffDescriptionCallback != null
            ? getBuffDescriptionCallback.Invoke(buffLang.Description)
            : buffLang.Description;

        var buffModule = __instance.buffModule;
        buffModule.RegisterBuff(
            buffLang.Visual,
            buffTitle,
            guid,
            out Il2CppSystem.Action<string, float> onBuffUpdate,
            out Il2CppSystem.Action<string, int> onBuffCountUpdate,
            out onBuffFinish);


        onBuffUpdate.Invoke(buffDescription, ConsistentBuffProgressFull);

        return false;
    }
}
