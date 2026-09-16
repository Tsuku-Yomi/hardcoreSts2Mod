using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using STS2RitsuLib.Utils.HarmonyIl;

namespace HardcoreSts2.Modifiers;


public sealed class SaveLoadCheckModifier : Modifier
{
    /// <summary>悔恨的次数上限，超过这个次数就轮到瓦库接管。</summary>
    private const int RegretMaxCount = 3;

    /// <summary>瓦库接管时的最大自动出牌数，与低语耳环保持一致。</summary>
    private const int VakuuMaxAutoPlayedCards = 13;

    public override string Id => "HardcoreSts2.SaveLoadCheck";

    public override string Name => "改造现实";

    public override string Description =>
        "Steven来尖塔狠狠惩罚SL了";

    private Harmony? _harmony;

    /// <summary>
    /// 常驻订阅读档事件：「改造现实」需要区分「读档回到战斗房间」和「正常走进新房间」，
    /// 这个订阅必须在读档发生之前就存在（读档时才启用 modifier 会错过本次事件），
    /// 所以放在一次性初始化里，早于存档开关恢复，且与 modifier 本身的开/关无关。
    /// </summary>
    protected override void OnInitialize()
    {
        RoomSlCounter.Initialize();
    }

    protected override void OnRegister()
    {
        RoomSlCounter.Attach();

        var target = AccessTools.Method(
            typeof(Hook),
            nameof(Hook.AfterAutoPrePlayPhaseEntered),
            new[] { typeof(HookPlayerChoiceContext), typeof(ICombatState), typeof(Player) });

        if (target is null)
        {
            Entry.Logger.Warn(
                $"[{nameof(SaveLoadCheckModifier)}] 找不到目标方法 Hook.AfterAutoPrePlayPhaseEntered，SL 惩罚不会生效。");
            return;
        }

        _harmony = new Harmony(Id);
        _harmony.Patch(
            target,
            postfix: new HarmonyMethod(
                AccessTools.Method(typeof(SaveLoadCheckModifier), nameof(ApplySlPenalty)))
            {
                // 排在其他自动出牌效果（如低语耳环、灌注）之后，避免多方同时抢手牌。
                priority = Priority.Last,
            });

        Entry.Logger.Info($"[{nameof(SaveLoadCheckModifier)}] SL 惩罚已接管战斗首回合。");
    }

    protected override void OnUnregister()
    {
        RoomSlCounter.Detach();

        if (_harmony is null)
        {
            return;
        }

        _harmony.UnpatchAll(_harmony.Id);
        _harmony = null;
        Entry.Logger.Info($"[{nameof(SaveLoadCheckModifier)}] 已撤销 SL 惩罚。");
    }

    /// <summary>
    /// 挂在 <see cref="Hook.AfterAutoPrePlayPhaseEntered"/> 之后：此时起始手牌已经抽完、还没进入出牌阶段，
    /// 是「往起始手牌 / 弃牌堆里塞临时卡」和「接管首回合」最稳的时机。
    /// <para>
    /// 目标方法是 async Task，必须用 <c>ref Task __result</c> 把后续逻辑接到原任务之后，
    /// 否则会在原任务完成前抢跑。
    /// </para>
    /// </summary>
    private static void ApplySlPenalty(
        HookPlayerChoiceContext __0,
        ICombatState __1,
        Player __2,
        ref Task __result)
    {
        var count = RoomSlCounter.CurrentRoomCount;
        if (__result is null || count <= 0 || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        __result = HarmonyAsyncTaskBridge.After(__result, () => ApplySlPenaltyAsync(__0, __1, __2, count));
    }

    private static async Task ApplySlPenaltyAsync(
        PlayerChoiceContext choiceContext,
        ICombatState combatState,
        Player player,
        int count)
    {
        // 惩罚只在战斗的第 1 回合生效。
        if (player.PlayerCombatState?.TurnNumber != 1)
        {
            return;
        }

        // 原任务跑完时战斗可能已经结束（首回合自伤致死、敌人被开场效果打死等）。
        // 这时候加牌没有意义，而且原版的加牌接口在战斗未进行时会走到越界分支，必须提前退出。
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        try
        {
            if (count == 1)
            {
                await AddTemporaryCardAsync<Dazed>(player, PileType.Hand);
            }
            else if (count <= RegretMaxCount)
            {
                await AddTemporaryCardAsync<Regret>(player, PileType.Discard);
            }
            else
            {
                await LetVakuuTakeOverAsync(choiceContext, combatState, player);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"[{nameof(SaveLoadCheckModifier)}] 施加 SL 惩罚失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 生成一张只属于本场战斗的卡并放进指定牌堆（不进牌组，战斗结束即回收）。
    /// <para>
    /// 这里必须用原版的 <see cref="CardPileCmd.AddToCombatAndPreview{T}"/> 而不是
    /// <c>AddGeneratedCardToCombat</c>：弃牌堆/抽牌堆按钮上的数量只监听
    /// <see cref="CardPile.CardAddFinished"/>，而该事件由卡的预览飞行（<c>NCardFlyVfx</c>）在落堆时触发。
    /// 直接塞进弃牌堆时原版不会创建飞行节点，事件永不触发，界面数量就会一直停在 0。
    /// </para>
    /// </summary>
    private static async Task AddTemporaryCardAsync<TCard>(Player player, PileType pileType)
        where TCard : CardModel
    {
        // 战斗结束时 AddGeneratedCardToCombat 内部会返回空数组再取 [0]（原版越界），也拿不到预览容器，
        // 所以这里再挡一层。
        if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead)
        {
            return;
        }

        await CardPileCmd.AddToCombatAndPreview<TCard>(player.Creature, pileType, 1, player);
    }

    /// <summary>
    /// 复刻低语耳环的负面效果：第一回合由瓦库替你自动打出最多 13 张能打的牌。
    /// </summary>
    private static async Task LetVakuuTakeOverAsync(
        PlayerChoiceContext choiceContext,
        ICombatState combatState,
        Player player)
    {
        var playerCombatState = player.PlayerCombatState;
        if (playerCombatState is null)
        {
            return;
        }

        var cardsPlayed = 0;
        var startTurn = playerCombatState.TurnNumber;

        using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
        {
            for (; cardsPlayed < VakuuMaxAutoPlayedCards; cardsPlayed++)
            {
                if (CombatManager.Instance.IsOverOrEnding
                    || CombatManager.Instance.IsPlayerReadyToEndTurn(player)
                    || playerCombatState.TurnNumber != startTurn)
                {
                    break;
                }

                var card = PileType.Hand.GetPile(player).Cards.FirstOrDefault(candidate => candidate.CanPlay());
                if (card is null)
                {
                    break;
                }

                var target = ResolveAutoPlayTarget(player, card, combatState);
                await card.SpendResources();
                await CardCmd.AutoPlay(choiceContext, card, target, AutoPlayType.Default, skipXCapture: true);
            }
        }

        if (cardsPlayed == 0)
        {
            return;
        }

        SpeakAsVakuu(player, cardsPlayed >= VakuuMaxAutoPlayedCards);
    }

    /// <summary>瓦库自动出牌的选目标规则，与原版遗物一致：敌人取最左，友方随机，其余取自己。</summary>
    private static Creature? ResolveAutoPlayTarget(Player player, CardModel card, ICombatState combatState) =>
        card.TargetType switch
        {
            TargetType.AnyEnemy => combatState.HittableEnemies.FirstOrDefault(),
            TargetType.AnyAlly => player.RunState.Rng.CombatTargets.NextItem(
                combatState.Allies.Where(ally =>
                    ally is not null && ally.IsAlive && ally.IsPlayer && ally != player.Creature)),
            TargetType.AnyPlayer => player.Creature,
            _ => null,
        };

    /// <summary>借用低语耳环的台词播报，让玩家知道这次是瓦库动的手。</summary>
    private static void SpeakAsVakuu(Player player, bool playedEverything)
    {
        try
        {
            var line = new LocString(
                "relics",
                playedEverything ? "WHISPERING_EARRING.warning" : "WHISPERING_EARRING.approval");
            TalkCmd.Play(line, player.Creature, VfxColor.Purple);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"[{nameof(SaveLoadCheckModifier)}] 播放瓦库台词失败：{ex.Message}");
        }
    }
}
