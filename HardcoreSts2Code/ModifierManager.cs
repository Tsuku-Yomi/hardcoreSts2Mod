using System.Reflection;
using MegaCrit.Sts2.Core.Logging;

namespace HardcoreSts2.Modifiers;

/// <summary>
/// modifier 管理器：通过反射自动发现当前程序集中的所有 <see cref="Modifier"/> 子类，
/// 并负责按需开启/关闭它们。
/// <para>
/// 与早期版本不同，发现阶段<strong>不会</strong>自动注册 modifier：
/// modifier 默认全部关闭，是否开启由角色选择界面的开关决定，
/// 该开关会写入存档，并在读档时按存档恢复。
/// </para>
/// </summary>
public static class ModifierManager
{
    private static readonly Dictionary<string, Modifier> _modifiers =
        new(StringComparer.Ordinal);

    /// <summary>发现顺序的 modifier Id 列表，保证 UI 与存档输出稳定有序。</summary>
    private static readonly List<string> _order = new();

    /// <summary>当前已发现的全部 modifier（只读，按发现顺序）。</summary>
    public static IReadOnlyDictionary<string, Modifier> Modifiers => _modifiers;

    /// <summary>按发现顺序返回全部 modifier。</summary>
    public static IReadOnlyList<Modifier> All =>
        _order.Select(id => _modifiers[id]).ToArray();

    /// <summary>任意 modifier 的开启状态发生变化时触发。</summary>
    public static event Action? StateChanged;

    /// <summary>
    /// 通过反射扫描指定程序集，自动实例化所有非抽象的 <see cref="Modifier"/> 子类。
    /// <para>
    /// 注意：此方法只做“发现/登记”，不会开启任何 modifier。
    /// 初始状态下所有 modifier 均为关闭，需由 <see cref="SetEnabled"/> 或
    /// <see cref="ApplyEnabledIds"/> 显式开启。
    /// </para>
    /// <para>
    /// 仅处理具备公共无参构造函数的类型；缺少无参构造函数的类型会被跳过并记录警告。
    /// 重复 Id 的 modifier 会被跳过，实例化过程中抛出的异常会被捕获并记录错误，
    /// 不会中断其余 modifier 的发现。
    /// </para>
    /// </summary>
    /// <param name="assembly">要扫描的程序集（通常是当前 mod 程序集）。</param>
    /// <param name="logger">可选的日志器；为 null 时不输出日志。</param>
    public static void Discover(Assembly assembly, Logger? logger = null)
    {
        foreach (var type in SafeGetTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            if (!typeof(Modifier).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null) is null)
            {
                logger?.Warn(
                    $"[ModifierManager] 跳过 {type.FullName}：缺少公共无参构造函数。");
                continue;
            }

            try
            {
                var modifier = (Modifier)Activator.CreateInstance(type)!;

                if (_modifiers.ContainsKey(modifier.Id))
                {
                    logger?.Warn(
                        $"[ModifierManager] 跳过重复 Id 的 modifier：{modifier.Id}（类型 {type.FullName}）。");
                    continue;
                }

                _modifiers[modifier.Id] = modifier;
                _order.Add(modifier.Id);
                logger?.Info(
                    $"[ModifierManager] 已发现 modifier：{modifier.Name}（{modifier.Id}），默认关闭。");
            }
            catch (Exception ex)
            {
                logger?.Error(
                    $"[ModifierManager] 发现 modifier 失败：{type.FullName},{ex.Message}。");
            }
        }
    }

    /// <summary>
    /// 对全部已发现的 modifier 执行一次性初始化（<see cref="Modifier.Initialize"/>），
    /// 应在 mod 加载阶段（<see cref="Discover"/> 之后）调用一次，之后不再调用。
    /// <para>
    /// 无论 modifier 当前是否开启都会初始化：初始化用于「开关之外」的常驻准备
    /// （如订阅游戏生命周期事件），必须早于任何开关状态恢复。
    /// 某个 modifier 初始化失败只会记录错误，不会影响其余 modifier，也不会中断加载。
    /// </para>
    /// </summary>
    public static void InitializeAll()
    {
        foreach (var id in _order)
        {
            try
            {
                _modifiers[id].Initialize();
            }
            catch (Exception ex)
            {
                Entry.Logger.Error($"[ModifierManager] 初始化 modifier 失败：{id},{ex.Message}。");
            }
        }
    }

    /// <summary>按 Id 获取已发现的 modifier；不存在则返回 null。</summary>
    public static Modifier? Get(string id) =>
        _modifiers.TryGetValue(id, out var modifier) ? modifier : null;

    /// <summary>指定的 modifier 当前是否已开启。</summary>
    public static bool IsEnabled(string id) => Get(id)?.IsRegistered ?? false;

    /// <summary>
    /// 开启或关闭指定 Id 的 modifier。
    /// </summary>
    /// <returns>Id 存在并成功处理时返回 true；Id 不存在或注册/反注册抛异常时返回 false。</returns>
    public static bool SetEnabled(string id, bool enabled)
    {
        var modifier = Get(id);
        if (modifier is null)
        {
            Entry.Logger.Warn($"[ModifierManager] 找不到 modifier：{id}。");
            return false;
        }

        if (modifier.IsRegistered == enabled)
        {
            return true;
        }

        try
        {
            if (enabled)
            {
                modifier.Register();
            }
            else
            {
                modifier.Unregister();
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error(
                $"[ModifierManager] {(enabled ? "开启" : "关闭")} modifier 失败：{id},{ex.Message}。");
            return false;
        }

        Entry.Logger.Info($"[ModifierManager] {(enabled ? "开启" : "关闭")} modifier：{id}。");
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 返回当前已开启的 modifier Id 列表（按发现顺序）。
    /// </summary>
    public static IReadOnlyList<string> GetEnabledIds() =>
        _order.Where(id => _modifiers[id].IsRegistered).ToArray();

    /// <summary>
    /// 把开启状态整体设置为 <paramref name="ids"/> 指定的集合：
    /// 集合内的开启，集合外的关闭。未登记的 Id 会被忽略。
    /// </summary>
    public static void ApplyEnabledIds(IEnumerable<string>? ids)
    {
        var target = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
        var changed = false;

        foreach (var id in _order)
        {
            var modifier = _modifiers[id];
            var wantEnabled = target.Contains(id);
            if (modifier.IsRegistered == wantEnabled)
            {
                continue;
            }

            try
            {
                if (wantEnabled)
                {
                    modifier.Register();
                }
                else
                {
                    modifier.Unregister();
                }

                changed = true;
            }
            catch (Exception ex)
            {
                Entry.Logger.Error(
                    $"[ModifierManager] 恢复 modifier 状态失败：{id},{ex.Message}。");
            }
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>关闭全部 modifier（回到默认状态）。</summary>
    public static void DisableAll() => ApplyEnabledIds(null);

    /// <summary>返回所有已发现 modifier 的简介结构体列表。</summary>
    public static IReadOnlyList<ModifierInfo> GetAllInfo() =>
        _order.Select(id => _modifiers[id].GetInfo()).ToArray();

    /// <summary>关闭全部 modifier（与 <see cref="Discover"/> 对称）。</summary>
    public static void UnregisterAll() => DisableAll();

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }
}
