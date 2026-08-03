using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Attributes;
using Il2CppSystem.Collections;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage.Collections;
using MetaMystia.ResourceEx.SpellCollection;
using MetaMystia.UI;
using NightScene.EventUtility;

namespace MetaMystia.ResourceEx.SpellCollection.Shinki;

/// <summary>
/// 神绮符卡主类：红卡「魔神降临」召唤魔界客人，黑卡「绮符·环游魔界80天」。
/// </summary>
[AutoLog]
public partial class Spell_Shinki : SpellBase
{
    internal const int ShinkiPortalBuffType = 9002;

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
    /// 注册神绮传送门常驻 Buff；描述回调传 null，由通用接管处用完好描述串兜底写入。
    /// </summary>
    [HideFromIl2Cpp]
    private void RegisterPortalBuff()
    {
        SpellHelper.RegisterConsistentBuff(
            Manager,
            (EventManager.BuffType)ShinkiPortalBuffType,
            null,
            null,
            out var onInterruptThisBuffCallback);
        SpellHelper.ShinkiPortalInterruptCallbacks.Add(onInterruptThisBuffCallback);
    }

    /// <summary>
    /// 移除神绮传送门常驻 Buff：主动中断红卡注册的全部传送门 Buff。
    /// </summary>
    [HideFromIl2Cpp]
    private void RemovePortalBuff()
    {
        SpellHelper.InterruptAllShinkiPortalBuffs();
    }
}
