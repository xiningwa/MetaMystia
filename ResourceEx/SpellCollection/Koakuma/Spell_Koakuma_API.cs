using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using DEYU.AssetHandleUtility;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage;
using GameData.CoreLanguage.Collections;
using MetaMystia.ResourceEx.SpellCollection;
using MetaMystia.ResourceEx.SpellCollection.Koakuma;
using MetaMystia.UI;
using MetaMiku;
using NightScene.EventUtility;

namespace MetaMystia;

public static partial class ResourceExManager
{
    // 小恶魔立绘目录前缀（poc 实测路径前缀 9001，红卡 Portrait/0.png）。
    private const string KoakumaPositivePortraitUri = "rex://ResourceExample/assets/Character/9001/Portrait/0.png";
    // 小恶魔 Buff 图标路径（poc 实测前缀 9001，红黑卡共用同一图标）。
    private const string KoakumaBuffIconUri = "rex://ResourceExample/assets/Buff/9001_1.png";
    // 符卡名/描述的语言版本数（红、黑两版）。
    private const int KoakumaSpellLanguageVersionCount = 2;

    // 符卡类型注入幂等标记：同一进程生命周期内只注入一次 il2cpp 类型，重复注入会抛异常。
    private static bool _koakumaSpellTypeInjected;

    /// <summary>
    /// 注册小恶魔符卡，使其可被夜场流程宣言；动态探测「小恶魔」稀客 id 作为符卡绑定 key，探测不到则跳过注册。
    /// </summary>
    public static void RegisterKoakumaSpell()
    {
        var koakumaGuestIds = AutoResolveKoakumaGuestIds();
        if (koakumaGuestIds.Count == 0)
        {
            Log.Warning("[Koakuma] 未探测到小恶魔稀客 id，跳过符卡注册。");
            return;
        }

        if (!_koakumaSpellTypeInjected)
        {
            ClassInjector.RegisterTypeInIl2Cpp<Spell_Koakuma>();
            _koakumaSpellTypeInjected = true;
        }

        foreach (var koakumaGuestId in koakumaGuestIds)
        {
            RegisterKoakumaSpellForGuest(koakumaGuestId);
        }
    }

    /// <summary>
    /// 为单个小恶魔稀客 id 装配符卡实例：创建实例、写入 SpecialGuestSpell、SpellLang（L10n）、立绘 Portrayal、CharacterHasSpell。
    /// </summary>
    /// <param name="guestId">目标小恶魔稀客 id。</param>
    private static void RegisterKoakumaSpellForGuest(int guestId)
    {
        var spell = ScriptableObject.CreateInstance<Spell_Koakuma>();
        var spellHandle = new Common.SceneDirector.RuntimeHandle<SpellBase>(spell);
        DataBaseNight.SpecialGuestSpell[guestId] = spellHandle.Cast<IAssetHandle<SpellBase>>();

        var langs = new Il2CppReferenceArray<LanguageBase>(KoakumaSpellLanguageVersionCount);
        langs[0] = new LanguageBase(TextId.Spell_Koakuma_NameRed.Get(), TextId.Spell_Koakuma_DescRed.Get());
        langs[1] = new LanguageBase(TextId.Spell_Koakuma_NameBlack.Get(), TextId.Spell_Koakuma_DescBlack.Get());
        GameData.CoreLanguage.Collections.DataBaseLanguage.SpellLang[guestId] = langs;

        if (TryGetSprite(KoakumaPositivePortraitUri, out var positiveSprite) && positiveSprite != null)
        {
            var spriteAssetHandle = new Common.SceneDirector.RuntimeHandle<Sprite>(positiveSprite)
                .Cast<IAssetHandle<Sprite>>();
            var tuple = new Il2CppSystem.ValueTuple<IAssetHandle<Sprite>, IAssetHandle<Sprite>>(
                spriteAssetHandle, spriteAssetHandle);
            DataBaseNight.SpecialGuestSpellPortrayal.ForceAddOrUpdateValueTuple(guestId, tuple);
        }
        else
        {
            Log.Warning($"[Koakuma] 加载立绘 sprite 失败：{KoakumaPositivePortraitUri}");
        }

        DataBaseCharacter.CharacterHasSpell[guestId] = true;
    }

    /// <summary>
    /// 注册小恶魔符卡的自定义 Buff 描述与图标，供右下角 Buff 栏显示；
    /// </summary>
    public static void RegisterKoakumaBuff()
    {
        TryGetSprite(KoakumaBuffIconUri, out var buffIcon);
        if (buffIcon == null)
        {
            Log.Warning($"[Koakuma] 加载 Buff 图标失败：{KoakumaBuffIconUri}");
        }

        SpellHelper.RegisterBuffDescription(
            (EventManager.BuffType)Spell_Koakuma.KoakumaEchoBuffType,
            TextId.Spell_KoakumaEcho_BuffName.Get(),
            TextId.Spell_KoakumaEcho_BuffDesc.Get(),
            buffIcon);
        SpellHelper.RegisterBuffDescription(
            (EventManager.BuffType)Spell_Koakuma.KoakumaChaosBuffType,
            TextId.Spell_KoakumaChaos_BuffName.Get(),
            TextId.Spell_KoakumaChaos_BuffDesc.Get(),
            buffIcon);
    }

    /// <summary>
    /// 按角色显示名「小恶魔」从角色配置探测全部小恶魔稀客 id 作为符卡绑定 key 集合；
    /// </summary>
    /// <returns>探测到的稀客 id 去重集合；未命中返回空集合。</returns>
    private static HashSet<int> AutoResolveKoakumaGuestIds()
    {
        var resolvedIds = new HashSet<int>();
        var specialGuests = DataBaseCharacter.SpecialGuest;
        if (specialGuests == null) return resolvedIds;

        foreach (var kvp in specialGuests)
        {
            var guest = kvp.Value;
            if (guest == null) continue;
            var displayName = guest.Text?.Name;
            if (!string.IsNullOrEmpty(displayName) && displayName.Contains("小恶魔"))
            {
                resolvedIds.Add(kvp.Key);
            }
        }

        return resolvedIds;
    }
}
