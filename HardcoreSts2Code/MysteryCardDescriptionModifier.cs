using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace HardcoreSts2.Modifiers;

public sealed class MysteryCardDescriptionModifier : Modifier
{
    public const string ReplacementText = "?!";

    public override string Id => "HardcoreSts2.MysteryCardDescription";
    public override string Name => "化智为空";
    public override string Description => "所有卡牌的描述都会被替换为「?!」。";

    private Harmony? _harmony;

    protected override void OnRegister()
    {
        _harmony = new Harmony(Id);
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(MysteryCardDescriptionModifier), nameof(ReplaceDescription)))
        {
            priority = Priority.Last,
        };

        foreach (var target in Targets)
        {
            if (target is null)
            {
                Entry.Logger.Warn(
                    $"[{nameof(MysteryCardDescriptionModifier)}] 找不到目标方法，跳过该补丁。");
                continue;
            }

            _harmony.Patch(target, postfix: postfix);
        }

        Entry.Logger.Info($"[{nameof(MysteryCardDescriptionModifier)}] 卡牌描述已全部替换为「{ReplacementText}」。");
    }

    protected override void OnUnregister()
    {
        if (_harmony is null)
        {
            return;
        }

        _harmony.UnpatchAll(_harmony.Id);
        _harmony = null;
        Entry.Logger.Info($"[{nameof(MysteryCardDescriptionModifier)}] 已撤销卡牌描述替换。");
    }

    private static IEnumerable<MethodBase?> Targets
    {
        get
        {
            yield return AccessTools.Method(
                typeof(CardModel),
                nameof(CardModel.GetDescriptionForPile),
                new[] { typeof(PileType), typeof(Creature) });
            yield return AccessTools.Method(
                typeof(CardModel),
                nameof(CardModel.GetDescriptionForUpgradePreview));
            yield return AccessTools.Method(
                typeof(CardModel),
                nameof(CardModel.GetDescriptionForPile),
                new[] { typeof(PileType), typeof(CardModel.DescriptionPreviewType),typeof(Creature) });
        }
    }

    private static void ReplaceDescription(ref string __result)
    {
        __result = ReplacementText;
    }
}
