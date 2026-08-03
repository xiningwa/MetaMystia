using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.Collections.Generic;
using UnityEngine;
using DEYU.AssetHandleUtility;
using GameData.Core.Collections.CharacterUtility;
using GameData.Core.Collections.NightSceneUtility;
using GameData.CoreLanguage;
using GameData.CoreLanguage.Collections;
using MetaMiku;
using MetaMystia.ResourceEx.SpellCollection;
using MetaMystia.ResourceEx.SpellCollection.Shinki;
using MetaMystia.UI;
using NightScene.EventUtility;

namespace MetaMystia;

public static partial class ResourceExManager
{
    // 神绮稀客的立绘目录前缀（poc 实测路径前缀 9004，红卡 Portrait/0.png、黑卡 Portrait/2.png）。
    private const string ShinkiPositivePortraitUri = "rex://ResourceExample/assets/Character/9004/Portrait/0.png";
    private const string ShinkiNegativePortraitUri = "rex://ResourceExample/assets/Character/9004/Portrait/2.png";
    // 神绮 Buff 图标路径（poc 实测前缀 9004）。
    private const string ShinkiBuffIconUri = "rex://ResourceExample/assets/Buff/9004_1.png";
    // 符卡名/描述的语言版本数（红、黑两版）。
    private const int ShinkiSpellLanguageVersionCount = 2;

    // ResourceEx 资源包自带神绮稀客 id（poc 实测路径前缀 9004 对应的稀客 id，与原版神绮 id 不同）。
    // 该 id 同样按名字「神绮」存在于 DataBaseCharacter.SpecialGuest，此处仅作显式兜底，确保两 id 都被注册。
    private const int ShinkiResourceExGuestId = 9004;

    // 符卡类型注入幂等标记：同一进程生命周期内只注入一次 il2cpp 类型，重复注入会抛异常。
    private static bool _shinkiSpellTypeInjected;

    /// <summary>
    /// 注册神绮符卡，使其可被夜场流程宣言；多稀客 id 各创建独立符卡实例以避免状态串扰。
    /// </summary>
    public static void RegisterShinkiSpell()
    {
        var shinkiGuestIds = AutoResolveShinkiGuestIds();
        if (shinkiGuestIds.Count == 0)
        {
            Log.Warning("[Shinki] 未探测到神绮稀客 id，跳过符卡注册。");
            return;
        }

        if (!_shinkiSpellTypeInjected)
        {
            ClassInjector.RegisterTypeInIl2Cpp<Spell_Shinki>();
            _shinkiSpellTypeInjected = true;
        }

        foreach (var shinkiGuestId in shinkiGuestIds)
        {
            RegisterSpellForGuest(shinkiGuestId);
        }
    }

    /// <summary>
    /// 为单个神绮稀客 id 装配符卡实例：创建实例、写入 SpecialGuestSpell、SpellLang（L10n）、立绘 Portrayal、CharacterHasSpell。
    /// 每个 id 独立创建一个 ScriptableObject 实例，避免多 id 共享同一实例造成的状态串扰。
    /// </summary>
    /// <param name="guestId">目标神绮稀客 id。</param>
    private static void RegisterSpellForGuest(int guestId)
    {
        var spell = ScriptableObject.CreateInstance<Spell_Shinki>();
        var spellHandle = new Common.SceneDirector.RuntimeHandle<SpellBase>(spell);
        DataBaseNight.SpecialGuestSpell[guestId] = spellHandle.Cast<IAssetHandle<SpellBase>>();

        var langs = new Il2CppReferenceArray<LanguageBase>(ShinkiSpellLanguageVersionCount);
        langs[0] = new LanguageBase(TextId.Spell_Shinki_NameRed.Get(), TextId.Spell_Shinki_DescRed.Get());
        langs[1] = new LanguageBase(TextId.Spell_Shinki_NameBlack.Get(), TextId.Spell_Shinki_DescBlack.Get());
        GameData.CoreLanguage.Collections.DataBaseLanguage.SpellLang[guestId] = langs;

        if (TryGetSprite(ShinkiPositivePortraitUri, out var positiveSprite) && positiveSprite != null)
        {
            var negativeSprite = positiveSprite;
            if (TryGetSprite(ShinkiNegativePortraitUri, out var loadedNegative) && loadedNegative != null)
            {
                negativeSprite = loadedNegative;
            }
            var spriteAssetHandle = new Common.SceneDirector.RuntimeHandle<Sprite>(positiveSprite)
                .Cast<IAssetHandle<Sprite>>();
            var negativeHandle = new Common.SceneDirector.RuntimeHandle<Sprite>(negativeSprite)
                .Cast<IAssetHandle<Sprite>>();
            var tuple = new Il2CppSystem.ValueTuple<IAssetHandle<Sprite>, IAssetHandle<Sprite>>(
                spriteAssetHandle, negativeHandle);
            DataBaseNight.SpecialGuestSpellPortrayal.ForceAddOrUpdateValueTuple(guestId, tuple);
        }
        else
        {
            Log.Warning($"[Shinki] 加载立绘 sprite 失败：{ShinkiPositivePortraitUri}");
        }

        DataBaseCharacter.CharacterHasSpell[guestId] = true;
    }

    /// <summary>
    /// 注册神绮符卡的自定义 Buff 描述与图标，供右下角 Buff 栏显示；常驻形态描述固定不显示剩余时间。
    /// </summary>
    public static void RegisterShinkiBuff()
    {
        TryGetSprite(ShinkiBuffIconUri, out var buffIcon);
        if (buffIcon == null)
        {
            Log.Warning($"[Shinki] 加载 Buff 图标失败：{ShinkiBuffIconUri}");
        }

        var shinkiBuffDesc = TextId.Spell_Shinki_BuffDesc.Get();
        SpellHelper.RegisterBuffDescription(
            (EventManager.BuffType)Spell_Shinki.ShinkiPortalBuffType,
            TextId.Spell_Shinki_BuffName.Get(),
            shinkiBuffDesc,
            buffIcon);
    }

    /// <summary>
    /// 按角色显示名「神绮」从角色配置探测全部神绮稀客 id 作为符卡绑定 key 集合；动态探测避免写死 id，并兜底加入 ResourceEx 自带神绮 id（9004）。
    /// </summary>
    /// <returns>探测到的神绮稀客 id 去重集合；未命中返回空集合。</returns>
    private static HashSet<int> AutoResolveShinkiGuestIds()
    {
        var resolvedIds = new HashSet<int>();
        var specialGuests = DataBaseCharacter.SpecialGuest;
        if (specialGuests != null)
        {
            foreach (var kvp in specialGuests)
            {
                var guest = kvp.Value;
                if (guest == null) continue;
                var displayName = guest.Text?.Name;
                if (!string.IsNullOrEmpty(displayName) && displayName.Contains("神绮"))
                {
                    resolvedIds.Add(kvp.Key);
                }
            }
        }

        // 显式兜底：ResourceEx 自带神绮 id（9004）即使名字探测未命中也确保注册。
        resolvedIds.Add(ShinkiResourceExGuestId);
        return resolvedIds;
    }
}
