using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace HardcoreSts2.Modifiers;

public sealed class HiddenMapPathsModifier : Modifier
{
    public override string Id => "HardcoreSts2.HiddenMapPaths";

    public override string Name => "迂回";

    public override string Description => "地图上房间之间的路径全部不可见";

    private Harmony? _harmony;

    protected override void OnRegister()
    {
        var target = AccessTools.Method(
            typeof(NMapScreen),
            "CreatePath",
            new[] { typeof(Vector2), typeof(Vector2) });

        if (target is null)
        {
            Entry.Logger.Warn(
                $"[{nameof(HiddenMapPathsModifier)}] 找不到目标方法 NMapScreen.CreatePath，补丁未生效。");
            return;
        }

        _harmony = new Harmony(Id);
        _harmony.Patch(
            target,
            postfix: new HarmonyMethod(
                AccessTools.Method(typeof(HiddenMapPathsModifier), nameof(HidePathDots))));

        Entry.Logger.Info($"[{nameof(HiddenMapPathsModifier)}] 地图路径已隐藏。");
    }

    protected override void OnUnregister()
    {
        if (_harmony is null)
        {
            return;
        }

        _harmony.UnpatchAll(_harmony.Id);
        _harmony = null;
        Entry.Logger.Info($"[{nameof(HiddenMapPathsModifier)}] 已撤销地图路径隐藏。");
    }

    private static void HidePathDots(IReadOnlyList<TextureRect> __result)
    {
        foreach (var dot in __result)
        {
            if (GodotObject.IsInstanceValid(dot))
            {
                dot.Visible = false;
            }
        }
    }
}
