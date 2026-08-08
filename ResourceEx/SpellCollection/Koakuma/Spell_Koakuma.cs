using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Attributes;
using Il2CppSystem.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using MetaMystia.ResourceEx.SpellCollection;
using NightScene.EventUtility;

namespace MetaMystia.ResourceEx.SpellCollection.Koakuma;

/// <summary>
/// 小恶魔符卡主类：红卡「灵符·遗失典籍的回响」、黑卡「幻符·献给巴瓦鲁的镇魂曲」。
/// 本文件仅承载符卡本体，Buff 描述注册见 Spell_Koakuma_API。
/// </summary>
[AutoLog]
public partial class Spell_Koakuma : SpellBase
{
    // 小恶魔回响 Buff 类型值
    internal const int KoakumaEchoBuffType = 9003;
    // 小恶魔混沌 Buff 类型值
    internal const int KoakumaChaosBuffType = 9005;
    // 红卡回响 Buff 计数（次）
    private const int KoakumaEchoCount = 3;
    // 黑卡混沌 Buff 持续秒数
    private const int KoakumaChaosDurationSeconds = 30;

    /// <summary>
    /// 返回符卡归属角色标识，供宣言日志与立绘偏移识别使用。
    /// 标识统一取自 SpellHelper.KoakumaOwnerIdentifier，保证与立绘偏移表键一致。
    /// </summary>
    /// <returns>归属角色标识字符串</returns>
    public override string OnGettingSpellOwnerIdentifier()
    {
        return SpellHelper.KoakumaOwnerIdentifier;
    }

    /// <summary>
    /// 宣言演出即将播放时被原生流程调用一次。
    /// </summary>
    /// <param name="isPositiveSpell">本次宣言是否为红卡（true）/黑卡（false）</param>
    /// <returns>是否允许游戏自动播放符卡宣言演出</returns>
    public override bool ShouldCallSpellDeclarationAuto(bool isPositiveSpell)
    {
        SpellHelper.SetCutinShift(SpellHelper.KoakumaOwnerIdentifier);
        return true;
    }

    /// <summary>
    /// 红卡「灵符·遗失典籍的回响」效果入口：激活回响 Buff 以揭示稀客点单 tag。
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，提供角色与回调等信息</param>
    /// <returns>il2cpp 协程迭代器</returns>
    public override IEnumerator OnPositiveBuffExecute(SpellExecutionContext spellExecutionContext)
    {
        return PositiveBuffRoutine().WrapToIl2Cpp();
    }

    /// <summary>
    /// 红卡主协程
    /// </summary>
    /// <returns>托管协程迭代器</returns>
    [HideFromIl2Cpp]
    private System.Collections.IEnumerator PositiveBuffRoutine()
    {
        Log.LogInfo("[Koakuma] 红卡【灵符·遗失典籍的回响】触发，激活回响 Buff（9003）");
        SpellHelper.RegisterCountedBuff(
            Manager,
            KoakumaEchoCount,
            EventManager.MathOperation.Set,
            (EventManager.BuffType)KoakumaEchoBuffType,
            OnEchoBuffDeduct);
        yield break;
    }

    /// <summary>
    /// 回响 Buff 每次扣除的占位回调
    /// </summary>
    [HideFromIl2Cpp]
    private static void OnEchoBuffDeduct()
    {
    }

    /// <summary>
    /// 黑卡「幻符·献给巴瓦鲁的镇魂曲」效果入口：激活混沌效果（食材顺序打乱、厨具随机化）。
    /// </summary>
    /// <param name="spellExecutionContext">符卡执行上下文，提供角色与回调等信息</param>
    /// <returns>il2cpp 协程迭代器</returns>
    public override IEnumerator OnNegativeBuffExecute(SpellExecutionContext spellExecutionContext)
    {
        return NegativeBuffRoutine().WrapToIl2Cpp();
    }

    /// <summary>
    /// 黑卡主协程

    /// </summary>
    /// <returns>托管协程迭代器</returns>
    [HideFromIl2Cpp]
    private System.Collections.IEnumerator NegativeBuffRoutine()
    {
        Log.LogInfo("[Koakuma] 黑卡【幻符·献给巴瓦鲁的镇魂曲】触发，激活混沌 Buff（9005）");
        SpellHelper.RegisterTimedBuff(
            Manager,
            KoakumaChaosDurationSeconds,
            (EventManager.BuffType)KoakumaChaosBuffType,
            out _);
        yield break;
    }
}
