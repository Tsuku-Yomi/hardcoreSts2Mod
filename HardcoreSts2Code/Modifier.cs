using System.Reflection;

namespace HardcoreSts2.Modifiers;

/// <summary>
/// modifier 的简介结构体，由 <see cref="Modifier.GetInfo"/> 返回。
/// 使用 record struct，便于值比较与解构。
/// </summary>
public readonly record struct ModifierInfo(
    /// <summary>modifier 的唯一标识（建议与注册到游戏中的内容 id 保持一致）。</summary>
    string Id,
    /// <summary>展示名称。</summary>
    string Name,
    /// <summary>简介描述。</summary>
    string Description
);

/// <summary>
/// 所有 modifier 的抽象基类。
/// <para>
/// 提供四个核心能力：
/// 1. <see cref="Register"/> —— 注册方法，把本 modifier 的修改/组件注册到游戏中；
/// 2. <see cref="Unregister"/> —— 解除注册方法，撤销 Register 造成的全部修改；
/// 3. <see cref="Initialize"/> —— 一次性初始化方法，在 mod 加载时调用且之后不再调用，
///    用于需要常驻订阅游戏生命周期事件等「必须在开关之前就位」的准备工作；
/// 4. <see cref="GetInfo"/> —— 简介方法，返回包含本 modifier 简介的 <see cref="ModifierInfo"/> 结构体。
/// </para>
/// 子类只需实现 <see cref="OnRegister"/> 与 <see cref="OnUnregister"/>，
/// 有一次性初始化需求时再额外覆写 <see cref="OnInitialize"/>；
/// 基类负责重复注册/解除注册/初始化的防护与状态维护。
/// </summary>
public abstract class Modifier
{
    /// <summary>modifier 的唯一标识，建议与注册到游戏中的内容 id 保持一致。</summary>
    public abstract string Id { get; }

    /// <summary>展示名称（会在简介中显示）。</summary>
    public abstract string Name { get; }

    /// <summary>简介描述（会在简介中显示）。</summary>
    public abstract string Description { get; }

    /// <summary>当前是否已经注册。Register 成功后变为 true，Unregister 成功后变为 false。</summary>
    public bool IsRegistered { get; private set; }

    /// <summary>是否已完成一次性初始化。Initialize 成功后变为 true，且不会再回到 false。</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// 注册方法：把本 modifier 的修改、组件等注册到游戏中。
    /// 重复调用会被安全忽略（不会二次注册）。
    /// </summary>
    public void Register()
    {
        if (IsRegistered)
        {
            return;
        }

        OnRegister();
        IsRegistered = true;
    }

    /// <summary>
    /// 解除注册方法：撤销 <see cref="Register"/> 造成的全部修改。
    /// 未注册时调用会被安全忽略。
    /// </summary>
    public void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        OnUnregister();
        IsRegistered = false;
    }

    /// <summary>
    /// 一次性初始化方法：在 mod 加载时由 <see cref="ModifierManager"/> 调用，
    /// 之后不再调用（重复调用会被安全忽略）。
    /// <para>
    /// 与 <see cref="Register"/> 的区别：初始化发生在任何开关出现之前，且只有一次，
    /// 因此不能放在 <see cref="OnRegister"/> 里的准备工作（例如常驻订阅游戏生命周期事件、
    /// 因为读档时才启用 modifier 会错过本次事件）应改放 <see cref="OnInitialize"/>。
    /// </para>
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        OnInitialize();
        IsInitialized = true;
    }

    /// <summary>
    /// 子类在此实现具体的注册逻辑（如注册卡牌、遗物、组件、Harmony patch 等）。
    /// </summary>
    protected abstract void OnRegister();

    /// <summary>
    /// 子类在此实现具体的反注册逻辑，必须与 <see cref="OnRegister"/> 严格对称，
    /// 以确保 Unregister 能完整撤销注册造成的修改。
    /// </summary>
    protected abstract void OnUnregister();

    /// <summary>
    /// 子类在此实现只需执行一次的初始化逻辑，默认什么都不做。
    /// <para>
    /// 调用时机是 mod 加载（<see cref="ModifierManager.InitializeAll"/>），此时该 modifier 通常还处于关闭状态，
    /// 且整个运行期只会执行一次；因此这里只应做与开关无关、且不可重复的准备工作。
    /// </para>
    /// </summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>
    /// 简介方法：返回包含本 modifier 简介的 <see cref="ModifierInfo"/> 结构体。
    /// </summary>
    public ModifierInfo GetInfo() =>
        new(Id, Name, Description);
}
