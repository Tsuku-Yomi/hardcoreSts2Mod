using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace HardcoreSts2.Modifiers;

/// <summary>
/// 把 modifier 的开关状态接入游戏存档的中转层。
/// <para>
/// 工作方式：
/// 1. 开局前（角色选择界面）通过 <see cref="StageToLobby"/> 把当前开关写入
///    RitsuLib 的“开局大厅暂存区”，开局时随新一局的存档快照一起提交；
/// 2. 读档时监听 <see cref="RunLoadedEvent"/>，把存档中的开关状态回放到
///    <see cref="ModifierManager"/> 上。
/// </para>
/// <para>
/// 存档槽位 key 一经发布<strong>不可修改</strong>，否则老玩家的这部分存档会丢失。
/// </para>
/// </summary>
public static class ModifierRunData
{
    /// <summary>存档槽位 key，作为游戏存档中识别该数据的唯一标识。</summary>
    public const string SlotKey = "tsk_hardcore_modifiers";

    private static RunSavedData<ModifierSaveData>? _slot;

    /// <summary>
    /// 注册存档槽位并订阅读档事件。应在 mod 初始化时调用一次。
    /// </summary>
    public static void Initialize()
    {
        try
        {
            using (RitsuLibFramework.BeginModDataRegistration(Entry.ModId))
            {
                _slot = RitsuLibFramework.GetRunSavedDataStore(Entry.ModId)
                    .Register(
                        key: SlotKey,
                        defaultFactory: static () => new ModifierSaveData(),
                        options: new RunSavedDataOptions
                        {
                            // 默认值和当前值一致（全部关闭）时不写入存档，保持存档干净；
                            // 读档时取不到即回退到默认值，同样是“全部关闭”。
                            WritePolicy = RunSavedDataWritePolicy.WhenNonDefault,
                            // 多人游戏里在大厅改动时同步给主机/队友。
                            SyncLobbyOnChange = true,
                        });
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"[ModifierRunData] 注册 modifier 存档槽位失败：{ex.Message}");
            _slot = null;
            return;
        }

        // 读档：按存档恢复开关状态。
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(OnRunLoaded, replayCurrentState: false);

        // 新开一局：无论大厅暂存是否生效，都在快照导出前补齐一次当前选择，
        // 保证“界面显示的状态”和“写进存档的状态”始终一致。
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataPreparingEvent>(OnPreparing);

        Entry.Logger.Info($"[ModifierRunData] modifier 存档槽位已注册（{Entry.ModId}::{SlotKey}）。");
    }

    /// <summary>
    /// 新一局快照导出前，把当前开关状态写入本局数据。
    /// 多人游戏由大厅暂存 / 主机权威快照负责，这里只兜底单人局。
    /// </summary>
    private static void OnPreparing(RunSavedDataPreparingEvent evt)
    {
        if (_slot is null || evt.IsMultiplayer)
        {
            return;
        }

        try
        {
            _slot.Set(evt.RunState, Capture());
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"[ModifierRunData] 写入新一局 modifier 状态失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 把当前开关状态写入开局大厅暂存区。开局时会随新一局的存档快照提交。
    /// <para>全部关闭时不写入，让存档保持默认（读档即为全部关闭）。</para>
    /// </summary>
    /// <param name="lobby">角色选择界面所属的开局大厅；为 null 时安全忽略。</param>
    public static void StageToLobby(StartRunLobby? lobby)
    {
        if (_slot is null || lobby is null)
        {
            return;
        }

        try
        {
            var data = Capture();
            if (data.EnabledIds.Count == 0)
            {
                _slot.Lobby.Remove(lobby);
                return;
            }

            _slot.Lobby.Set(lobby, data);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"[ModifierRunData] 写入 modifier 开局暂存失败：{ex.Message}");
        }
    }

    /// <summary>把当前 modifier 开关状态快照成一个存档对象。</summary>
    private static ModifierSaveData Capture() =>
        new() { EnabledIds = ModifierManager.GetEnabledIds().ToList() };

    /// <summary>
    /// 读档：按存档里记录的开关状态，选择性地开启/关闭各个 modifier。
    /// </summary>
    private static void OnRunLoaded(RunLoadedEvent evt)
    {
        if (_slot is null)
        {
            ModifierManager.DisableAll();
            return;
        }

        ModifierSaveData saved;
        try
        {
            saved = _slot.Get(evt.RunState);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"[ModifierRunData] 读取 modifier 存档失败：{ex.Message}");
            ModifierManager.DisableAll();
            return;
        }

        ModifierManager.ApplyEnabledIds(saved.EnabledIds);
        Entry.Logger.Info(
            $"[ModifierRunData] 已按存档恢复 modifier：{(saved.EnabledIds.Count == 0 ? "（无）" : string.Join(", ", saved.EnabledIds))}。");
    }
}
