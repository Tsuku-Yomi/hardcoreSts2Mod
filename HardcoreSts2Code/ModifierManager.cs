using System.Reflection;
using MegaCrit.Sts2.Core.Logging;

namespace HardcoreSts2.Modifiers;

/// <summary>
/// modifier 管理器：通过反射自动发现并注册当前程序集中的所有 <see cref="Modifier"/> 子类。
/// </summary>
public static class ModifierManager
{
    private static readonly Dictionary<string, Modifier> _modifiers =
        new(StringComparer.Ordinal);

    /// <summary>当前已发现的全部 modifier（只读）。</summary>
    public static IReadOnlyDictionary<string, Modifier> Modifiers => _modifiers;

    /// <summary>
    /// 通过反射扫描指定程序集，自动实例化并注册所有非抽象的 <see cref="Modifier"/> 子类。
    /// <para>
    /// 仅处理具备公共无参构造函数的类型；缺少无参构造函数的类型会被跳过并记录警告。
    /// 重复 Id 的 modifier 会被跳过，实例化或注册过程中抛出的异常会被捕获并记录错误，
    /// 不会中断其余 modifier 的注册。
    /// </para>
    /// </summary>
    /// <param name="assembly">要扫描的程序集（通常是当前 mod 程序集）。</param>
    /// <param name="logger">可选的日志器；为 null 时不输出日志。</param>
    public static void AutoRegister(Assembly assembly, Logger? logger = null)
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
                modifier.Register();
                logger?.Info(
                    $"[ModifierManager] 已注册 modifier：{modifier.Name}（{modifier.Id}）。");
            }
            catch (Exception ex)
            {
                logger?.Error(
                    $"[ModifierManager] 注册 modifier 失败：{type.FullName},{ex.Message}。");
            }
        }
    }

    /// <summary>按 Id 获取已注册的 modifier；不存在则返回 null。</summary>
    public static Modifier? Get(string id) =>
        _modifiers.TryGetValue(id, out var modifier) ? modifier : null;

    /// <summary>返回所有已注册 modifier 的简介结构体列表。</summary>
    public static IReadOnlyList<ModifierInfo> GetAllInfo() =>
        _modifiers.Values.Select(m => m.GetInfo()).ToArray();

    /// <summary>解除注册所有已注册的 modifier（与 <see cref="AutoRegister"/> 对称）。</summary>
    public static void UnregisterAll()
    {
        foreach (var modifier in _modifiers.Values)
        {
            modifier.Unregister();
        }
    }

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
