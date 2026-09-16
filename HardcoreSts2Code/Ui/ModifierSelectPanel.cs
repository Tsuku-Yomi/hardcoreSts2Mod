using Godot;
using HardcoreSts2.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace HardcoreSts2.Ui;

/// <summary>
/// 角色选择界面右上角的 modifier 开关面板。
/// <para>
/// 界面结构（面板、标题、提示、分隔线、空状态、每一行开关）全部在场景资源
/// <c>res://HardcoreSts2/scenes/ui/modifier_select_panel.tscn</c> 与
/// <c>modifier_select_row.tscn</c> 中定义，本脚本只负责：
/// 1. 按 <see cref="ModifierManager"/> 发现的 modifier 数量实例化行场景并填入名称/描述；
/// 2. 勾选 → 开关 modifier，并把选择暂存到本局存档；
/// 3. 界面显示时按当前状态刷新勾选。
/// </para>
/// <para>
/// 场景通过 <c>ModNodeAttachmentRegistry</c> 附加到 <see cref="NCharacterSelectScreen"/> 的
/// <c>_Ready</c> 之后。
/// </para>
/// </summary>
public sealed partial class ModifierSelectPanel : PanelContainer
{
    /// <summary>面板节点名，用于节点附加的去重策略。</summary>
    public const string NodeName = "ModifierSelectPanel";

    /// <summary>面板场景路径，供节点附加注册使用。</summary>
    public const string ScenePath = "res://HardcoreSts2/scenes/ui/modifier_select_panel.tscn";

    /// <summary>单行开关的场景路径。</summary>
    private const string RowScenePath = "res://HardcoreSts2/scenes/ui/modifier_select_row.tscn";

    /// <summary>游戏 UI 正文字体；mod 工程里没有该资源，只能运行时从游戏 pck 取。</summary>
    private const string GameFontPath = "res://themes/kreon_regular_shared.tres";

    /// <summary>滚动窗口最多同时显示的条目数，超出后出现竖向滚动条。</summary>
    private const int MaxVisibleRows = 4;

    private readonly Dictionary<string, CheckBox> _toggles = new(StringComparer.Ordinal);

    private VBoxContainer? _list;
    private ScrollContainer? _scroll;
    private Label? _emptyState;
    private NCharacterSelectScreen? _screen;
    private Callable? _visibilityCallable;
    private bool _suppressToggle;

    /// <inheritdoc />
    public override void _Ready()
    {
        _list = GetNode<VBoxContainer>("%ModifierList");
        _scroll = GetNode<ScrollContainer>("%ModifierScroll");
        _emptyState = GetNode<Label>("%EmptyState");

        // 字体先应用，后面量行高时才是最终字号下的高度。
        ApplyGameFont();
        BuildRows();
        UpdateScrollViewport();
        WireFocusNeighbors();
        RefreshFromManager();
    }

    /// <summary>把面板绑定到角色选择界面：同步开关状态并在界面显示时刷新。</summary>
    public void Bind(NCharacterSelectScreen screen)
    {
        _screen = screen;
        _visibilityCallable ??= Callable.From(OnScreenVisibilityChanged);

        if (!screen.IsConnected(Control.SignalName.VisibilityChanged, _visibilityCallable.Value))
        {
            screen.Connect(Control.SignalName.VisibilityChanged, _visibilityCallable.Value);
        }

        // 正常流程下 setup 早于本节点入树，_Ready 会再刷新一次；
        // 复用已存在节点的场景下需要在这里补一次。
        if (IsNodeReady())
        {
            RefreshFromManager();
        }
    }

    /// <summary>按当前发现的 modifier 实例化开关行。</summary>
    private void BuildRows()
    {
        var modifiers = ModifierManager.All;
        if (_list is null || _emptyState is null)
        {
            return;
        }

        _emptyState.Visible = modifiers.Count == 0;
        if (modifiers.Count == 0)
        {
            return;
        }

        var rowScene = ResourceLoader.Load<PackedScene>(RowScenePath);
        if (rowScene is null)
        {
            Entry.Logger.Error($"[{nameof(ModifierSelectPanel)}] 找不到行场景：{RowScenePath}。");
            return;
        }

        foreach (var modifier in modifiers)
        {
            var row = rowScene.Instantiate();
            var id = modifier.Id;

            var toggle = row.GetNode<CheckBox>("%Toggle");
            toggle.Text = modifier.Name;
            toggle.TooltipText = modifier.Description;
            toggle.ButtonPressed = modifier.IsRegistered;
            toggle.Connect(
                BaseButton.SignalName.Toggled,
                Callable.From<bool>(pressed => OnToggle(id, pressed)));

            row.GetNode<Label>("%Description").Text = modifier.Description;

            _list.AddChild(row);
            _toggles[id] = toggle;
        }
    }

    /// <summary>
    /// 滚动窗口最多同时显示 <see cref="MaxVisibleRows"/> 个条目：
    /// 条目数不超过上限时高度贴合内容，超出时固定为该高度并出现竖向滚动条。
    /// </summary>
    private void UpdateScrollViewport()
    {
        if (_scroll is null || _list is null)
        {
            return;
        }

        var rows = _list.GetChildren().OfType<Control>().ToArray();
        if (rows.Length == 0)
        {
            _scroll.CustomMinimumSize = Vector2.Zero;
            return;
        }

        var visible = Math.Min(rows.Length, MaxVisibleRows);
        var height = 0f;
        for (var i = 0; i < visible; i++)
        {
            height += rows[i].GetCombinedMinimumSize().Y;
        }

        height += _list.GetThemeConstant("separation") * (visible - 1);
        _scroll.CustomMinimumSize = new Vector2(0f, height);
    }

    /// <summary>
    /// 游戏 UI 字体不在本 mod 工程内：直接写进 .tscn 会被 Godot 编辑器当成失效资源丢掉，
    /// 所以运行时从游戏 pck 取一次，作为整个面板（含行）的默认字体；取不到就用引擎默认字体。
    /// </summary>
    private void ApplyGameFont()
    {
        if (!ResourceLoader.Exists(GameFontPath))
        {
            return;
        }

        var font = ResourceLoader.Load<Font>(GameFontPath);
        if (font is null)
        {
            return;
        }

        Theme = new Theme { DefaultFont = font };
    }

    /// <summary>
    /// 面板内的上下方向键焦点循环，方便手柄/键盘操作（需要节点已在场景树中）。
    /// 左右方向不设邻居，交给 Godot 自动寻找，避免把焦点困在面板里。
    /// </summary>
    private void WireFocusNeighbors()
    {
        var toggles = _toggles.Values.ToArray();
        if (toggles.Length == 0)
        {
            return;
        }

        for (var i = 0; i < toggles.Length; i++)
        {
            var previous = toggles[(i - 1 + toggles.Length) % toggles.Length];
            var next = toggles[(i + 1) % toggles.Length];
            toggles[i].FocusNeighborTop = previous.GetPath();
            toggles[i].FocusNeighborBottom = next.GetPath();
        }
    }

    private void OnScreenVisibilityChanged()
    {
        if (_screen is null || !IsInstanceValid(_screen) || !_screen.Visible)
        {
            return;
        }

        RefreshFromManager();

        // 每次进入角色选择界面，都把当前选择重新暂存到（新的）大厅会话，
        // 保证界面上显示的状态和最终写入存档的状态一致。
        ModifierRunData.StageToLobby(CurrentLobby());
    }

    private StartRunLobby? CurrentLobby() =>
        _screen is not null && IsInstanceValid(_screen) ? _screen.Lobby : null;

    private void RefreshFromManager()
    {
        _suppressToggle = true;
        try
        {
            foreach (var (id, toggle) in _toggles)
            {
                if (IsInstanceValid(toggle))
                {
                    toggle.ButtonPressed = ModifierManager.IsEnabled(id);
                }
            }
        }
        finally
        {
            _suppressToggle = false;
        }
    }

    private void OnToggle(string id, bool pressed)
    {
        if (_suppressToggle)
        {
            return;
        }

        if (!ModifierManager.SetEnabled(id, pressed))
        {
            // 开启/关闭失败时把界面回滚到真实状态。
            RefreshFromManager();
            return;
        }

        ModifierRunData.StageToLobby(CurrentLobby());
    }
}
