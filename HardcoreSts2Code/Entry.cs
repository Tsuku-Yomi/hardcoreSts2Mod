using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Scaffolding.Godot.NodeAttachments;
using HardcoreSts2.Modifiers;
using HardcoreSts2.Ui;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace HardcoreSts2;

[ModInitializer(nameof(Initialize))]
public partial class Entry
{
    // ModId 需要和 HardcoreSts2.json 里的 id 保持一致。
    // res://HardcoreSts2/... 里的 HardcoreSts2 是 PCK 资源目录，不是 C# namespace。
    public const string ModId = "HardcoreSts2";
    public const string ResPath = $"res://{ModId}";

    public static Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // 以下示例默认已经在 Entry.Initialize() 中调用了
        // RitsuLibFramework.EnsureGodotScriptsRegistered(...) 和
        // ModTypeDiscoveryHub.RegisterModAssembly(...)，否则自动注册不会生效。
        //
        // Godot C# 脚本注册只负责让 pck 中的脚本类型能被 Godot 找到。
        // 这一步和 RitsuLib 的内容自动注册不是同一件事，两个都需要保留。
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);

        // 自动注册扫描会读取当前程序集里的 RegisterCard/RegisterRelic 等 attribute。
        // 新增内容类后，只要 attribute 写对，通常不需要在入口里手动逐个注册。
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        // 通过反射自动发现当前程序集中的所有 Modifier 子类。
        // 只发现、不开启：modifier 默认全部关闭，是否开启由角色选择界面的开关决定。
        ModifierManager.Discover(assembly, Logger);

        // 注册 modifier 开关的存档槽位，并在读档时按存档恢复开关状态。
        ModifierRunData.Initialize();

        // 在角色选择界面挂载 modifier 开关面板。
        RegisterModifierSelectPanel();

        Logger.Info("HardcoreSts2 initialized.");
    }

    /// <summary>
    /// 把场景资源 <see cref="ModifierSelectPanel.ScenePath"/> 挂到
    /// <see cref="NCharacterSelectScreen"/> 的 <c>_Ready</c> 之后。
    /// </summary>
    private static void RegisterModifierSelectPanel()
    {
        try
        {
            ModNodeAttachmentRegistry.For(ModId)
                .RegisterReadyChildFromScene<NCharacterSelectScreen, ModifierSelectPanel>(
                    localId: "character_select_modifier_panel",
                    scenePath: ModifierSelectPanel.ScenePath,
                    setup: static (screen, panel) => panel.Bind(screen),
                    options: new NodeAttachmentOptions
                    {
                        Name = ModifierSelectPanel.NodeName,
                        DuplicatePolicy = NodeAttachmentDuplicatePolicy.ReuseExistingByName,
                    });
        }
        catch (Exception ex)
        {
            Logger.Error($"[HardcoreSts2] 挂载 modifier 开关面板失败：{ex.Message}");
        }
    }
}
