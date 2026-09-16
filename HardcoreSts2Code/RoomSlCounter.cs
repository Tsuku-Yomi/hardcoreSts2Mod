using Godot;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;

namespace HardcoreSts2.Modifiers;

/// <summary>
/// 「房间内战斗 SL 次数」的记账器：记录玩家在同一个房间里通过「退出读档」逃避了多少次战斗。
/// <para>计数规则：</para>
/// <para>1. 走进一个新房间时计数清零；</para>
/// <para>2. 读档后游戏会重建「存档所在的那个房间」，如果重建出来的是还没打完的战斗房间，
/// 就说明玩家是战斗中退出再进来的，计数 +1（同一房间反复 SL 会持续累加）。</para>
/// <para>
/// 为什么不用 RitsuLib 的 <c>RunSavedData</c> 存这个计数：游戏只在「进入房间前 / 战斗胜利后 / 事件结束」写档，
/// 战斗中途退出不写档，因此每次读档读到的都是同一份「刚进房间时」的快照，
/// 同一房间内的第 N 次 SL 在局内存档里根本留不下痕迹（永远会算出第 1 次）。
/// 所以这里把计数放进 mod 自己的小文件，每次变化立刻落盘。
/// </para>
/// </summary>
internal static class RoomSlCounter
{
    private const string SaveDir = "user://hardcore_sts2";
    private const string SavePath = SaveDir + "/room_sl_count.json";

    /// <summary>本次进房间是不是由「读档」触发的。</summary>
    private static bool _resumingFromSavedRun;

    /// <summary>读档标记的订阅必须常驻，因此与 modifier 的启停无关。</summary>
    private static IDisposable? _saveResumeSubscription;

    /// <summary>进房监听随 modifier 一起启停。</summary>
    private static IDisposable? _roomSubscription;

    /// <summary>当前房间内已发生的战斗 SL 次数；进入新房间时清零。</summary>
    public static int CurrentRoomCount { get; private set; }

    /// <summary>
    /// 常驻订阅读档事件，标记「接下来进入的房间是读档重建出来的」。
    /// <para>
    /// 由 <c>SaveLoadCheckModifier.OnInitialize</c> 在 mod 加载时调用一次
    /// （不能放在 <c>OnRegister</c> 里）：读档时 modifier 才被存档里的开关启用，
    /// 那时再订阅会错过本次 <see cref="RunLoadedEvent"/>（事件正在派发中），
    /// 于是就无法区分「读档回到战斗房间」和「正常走进新房间」。
    /// </para>
    /// </summary>
    public static void Initialize()
    {
        _saveResumeSubscription ??= RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(
            static _ => _resumingFromSavedRun = true,
            replayCurrentState: false);
    }

    /// <summary>开启记账（由 modifier 注册时调用）。</summary>
    public static void Attach()
    {
        _roomSubscription ??= RitsuLibFramework.SubscribeLifecycle<RoomEnteredEvent>(
            OnRoomEntered,
            replayCurrentState: false);
    }

    /// <summary>停止记账（由 modifier 反注册时调用）。</summary>
    public static void Detach()
    {
        _roomSubscription?.Dispose();
        _roomSubscription = null;
        _resumingFromSavedRun = false;
        CurrentRoomCount = 0;
    }

    private static void OnRoomEntered(RoomEnteredEvent evt)
    {
        var roomKey = BuildRoomKey(evt.RunState);
        var (savedRoomKey, savedCount) = Load();

        // 「读档重建出来的、还没打完的地图点战斗房间」＝ 玩家战斗中退出过，也就是一次 SL。
        // 另外两道校验都是为了兜底：
        // 1. 房间标识必须与记录一致 —— 读档重建的一定是存档里那个房间，正常走进新房间撞不上；
        // 2. ParentEventId 必须为空 —— 事件房里推出来的嵌套战斗和事件房共用同一个地图点（同一个
        //    房间标识），而读档只会重开事件、不会直接重开那场战斗。此外读档到「已打完的房间」时
        //    （CombatRoom.StartPreFinishedCombat 不发 Hook.AfterRoomEntered）读档标记不会被消费，
        //    会残留到下一次进房，这两道校验能保证它不会误伤。
        var resumedActiveCombat = _resumingFromSavedRun
            && evt.Room is CombatRoom combatRoom
            && !combatRoom.IsPreFinished
            && combatRoom.ParentEventId is null
            && string.Equals(savedRoomKey, roomKey, StringComparison.Ordinal);
        _resumingFromSavedRun = false;

        if (resumedActiveCombat)
        {
            CurrentRoomCount = savedCount + 1;
            Save(roomKey, CurrentRoomCount);
            Entry.Logger.Info($"[{nameof(RoomSlCounter)}] 检测到通过 SL 退出战斗：房间 {roomKey}，第 {CurrentRoomCount} 次。");
            return;
        }

        // 进入新房间（含读档回到商店 / 已打完的房间等情况）：计数清零。
        CurrentRoomCount = 0;
        Save(roomKey, 0);
    }

    /// <summary>用「章节 + 地图坐标」标识房间，读档前后保持一致。</summary>
    private static string BuildRoomKey(IRunState runState)
    {
        var coord = runState.CurrentMapCoord;
        return coord.HasValue
            ? $"{runState.CurrentActIndex}:{coord.Value.col},{coord.Value.row}"
            : $"{runState.CurrentActIndex}:?";
    }

    private static (string RoomKey, int Count) Load()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(SavePath))
            {
                return (string.Empty, 0);
            }

            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            if (file is null)
            {
                return (string.Empty, 0);
            }

            var json = new Json();
            if (json.Parse(file.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            {
                return (string.Empty, 0);
            }

            var data = json.Data.AsGodotDictionary();
            var roomKey = data.TryGetValue("room_key", out var key) ? key.AsString() : string.Empty;
            var count = data.TryGetValue("count", out var value) ? value.AsInt32() : 0;
            return (roomKey, Math.Max(count, 0));
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"[{nameof(RoomSlCounter)}] 读取 SL 计数文件失败：{ex.Message}");
            return (string.Empty, 0);
        }
    }

    private static void Save(string roomKey, int count)
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);

            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            if (file is null)
            {
                Entry.Logger.Warn($"[{nameof(RoomSlCounter)}] 无法写入 SL 计数文件：{SavePath}。");
                return;
            }

            var data = new Godot.Collections.Dictionary
            {
                { "room_key", roomKey },
                { "count", count },
            };
            file.StoreString(Json.Stringify(data));
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"[{nameof(RoomSlCounter)}] 写入 SL 计数文件失败：{ex.Message}");
        }
    }
}
