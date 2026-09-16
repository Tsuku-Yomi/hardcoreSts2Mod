namespace HardcoreSts2.Modifiers;

/// <summary>
/// 随存档保存的 modifier 开关状态。
/// <para>
/// 使用类而不是基元类型，便于后续新增字段而不必更换存档槽位。
/// 序列化由 RitsuLib 的 <c>RunSavedData</c> 负责（System.Text.Json，属性名保持原样）。
/// </para>
/// <para>
/// 默认值（空列表）表示全部关闭，因此“一个都没开”时不会写入存档，
/// 读档时回退到默认值即可得到“全部关闭”。
/// </para>
/// </summary>
public sealed class ModifierSaveData
{
    /// <summary>本局已开启的 modifier Id 列表。</summary>
    public List<string> EnabledIds { get; set; } = new();
}
