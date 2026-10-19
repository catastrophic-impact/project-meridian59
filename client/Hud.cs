using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

/// <summary>A floating HUD window: drag by the title bar, resize by the bottom-right grip, raised on click.</summary>
public partial class HudWindow : Control
{
    public string WindowId { get; }
    public bool Locked { get; set; }
    public bool Resizable { get; set; } = true;
    public Vector2 MinWindowSize { get; set; } = new Vector2(150, 70);
    public Control Content { get; }
    /// <summary>Windows may not go above this y (the top bar) - set by the HudManager.</summary>
    public Func<float> TopLimit { get; set; } = () => 0f;
    /// <summary>Raised when the user finished moving/resizing (so the layout can be saved).</summary>
    public event Action Changed;

    readonly Label titleLabel;
    readonly Control bar, grip;
    bool dragging, resizing;
    Vector2 grabOffset;

    public HudWindow(string id, string title, Control content)
    {
        WindowId = id;
        Content = content;
        MouseFilter = MouseFilterEnum.Pass;

        var bg = new Panel { MouseFilter = MouseFilterEnum.Pass };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, 0.80f),
            BorderColor = new Color(0.40f, 0.40f, 0.52f, 0.95f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        AddChild(bg);

        bar = new ColorRect { Color = new Color(0.16f, 0.16f, 0.26f, 0.95f), CustomMinimumSize = new Vector2(0, 22), MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = 22;
        titleLabel = new Label { Text = title, Position = new Vector2(8, 1), MouseFilter = MouseFilterEnum.Ignore };
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        bar.AddChild(titleLabel);
        AddChild(bar);

        content.ClipContents = true;
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.OffsetLeft = 6; content.OffsetTop = 26; content.OffsetRight = -6; content.OffsetBottom = -6;
        AddChild(content);

        grip = new Control { CustomMinimumSize = new Vector2(16, 16), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Fdiagsize };
        grip.SetAnchorsPreset(LayoutPreset.BottomRight);
        grip.OffsetLeft = -16; grip.OffsetTop = -16;
        grip.Draw += () =>
        {
            if (!Resizable || Locked) return;
            var c = new Color(0.7f, 0.7f, 0.85f, 0.8f);
            grip.DrawLine(new Vector2(14, 5), new Vector2(5, 14), c, 1);
            grip.DrawLine(new Vector2(14, 9), new Vector2(9, 14), c, 1);
        };
        AddChild(grip);

        bar.GuiInput += OnBarInput;
        grip.GuiInput += OnGripInput;
        ApplyLockVisuals();
    }

    public string TitleText => titleLabel.Text;
    public void SetTitle(string t) => titleLabel.Text = t;

    /// <summary>Add a small close button to the title bar (hides the window and saves the layout).</summary>
    public void AddCloseButton()
    {
        var x = new Button { Text = "x", Flat = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 20) };
        x.SetAnchorsPreset(LayoutPreset.TopRight);
        x.OffsetLeft = -24; x.OffsetTop = 1; x.OffsetRight = -2; x.OffsetBottom = 21;
        x.Pressed += () => { Visible = false; Changed?.Invoke(); };
        bar.AddChild(x);
    }

    public void SetLocked(bool locked)
    {
        Locked = locked;
        dragging = resizing = false;
        ApplyLockVisuals();
    }

    void ApplyLockVisuals()
    {
        bar.MouseDefaultCursorShape = Locked ? CursorShape.Arrow : CursorShape.Move;
        grip.Visible = Resizable;
        grip.QueueRedraw();
    }

    Vector2 ParentSize => GetParent<Control>()?.Size ?? new Vector2(1280, 720);

    void OnBarInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && !Locked) { dragging = true; grabOffset = GetParent<Control>().GetLocalMousePosition() - Position; GetParent().MoveChild(this, -1); }
            else if (!mb.Pressed && dragging) { dragging = false; Changed?.Invoke(); }
        }
        else if (e is InputEventMouseMotion && dragging)
        {
            var p = GetParent<Control>().GetLocalMousePosition() - grabOffset;
            var ps = ParentSize; float top = TopLimit();
            // the whole window stays inside the client area, below the top bar
            Position = new Vector2(Mathf.Clamp(p.X, 0, Mathf.Max(0, ps.X - Size.X)), Mathf.Clamp(p.Y, top, Mathf.Max(top, ps.Y - Size.Y)));
        }
    }

    void OnGripInput(InputEvent e)
    {
        if (!Resizable) return;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && !Locked) { resizing = true; grabOffset = GetParent<Control>().GetLocalMousePosition() - (Position + Size); GetParent().MoveChild(this, -1); }
            else if (!mb.Pressed && resizing) { resizing = false; Changed?.Invoke(); }
        }
        else if (e is InputEventMouseMotion && resizing)
        {
            var m = GetParent<Control>().GetLocalMousePosition() - grabOffset - Position;
            var ps = ParentSize;
            Size = new Vector2(Mathf.Clamp(m.X, MinWindowSize.X, ps.X - Position.X), Mathf.Clamp(m.Y, MinWindowSize.Y, ps.Y - Position.Y));
        }
    }

    public override void _Input(InputEvent e)
    {
        // clicking anywhere on a window brings it to the front (without consuming the click)
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && Visible && GetGlobalRect().HasPoint(GetGlobalMousePosition()))
            GetParent()?.MoveChild(this, -1);
    }
}

/// <summary>Owns all HUD windows: default layout, saving/loading, lock, UI scale, and the "Interface" settings window.</summary>
public class HudManager
{
    class WinState { public double X { get; set; } public double Y { get; set; } public double W { get; set; } public double H { get; set; } public bool Visible { get; set; } = true; }
    class LayoutFile
    {
        public Dictionary<string, WinState> Windows { get; set; } = new Dictionary<string, WinState>();
        public bool Locked { get; set; }
        public double Scale { get; set; } = 1.0;
    }

    public readonly Control Root;
    readonly Window window;
    readonly Dictionary<string, HudWindow> windows = new Dictionary<string, HudWindow>();
    readonly Dictionary<string, (Vector2 posFrac, Vector2 size)> defaults = new Dictionary<string, (Vector2, Vector2)>();
    readonly Dictionary<string, bool> defaultVisible = new Dictionary<string, bool>();
    readonly string savePath;
    LayoutFile layout = new LayoutFile();
    CheckButton lockCheck; HSlider scaleSlider; Label scaleLabel;
    bool suppressUi;

    readonly Dictionary<string, Vector2> frac = new Dictionary<string, Vector2>();
    /// <summary>Size of the UI area in UI units (window pixels divided by the UI scale).</summary>
    // (the viewport's visible rect already includes Godot's own stretch to the project base size, e.g. fullscreen)
    Vector2 UiSize => Root.IsInsideTree() ? Root.GetViewportRect().Size : new Vector2(window.Size.X, window.Size.Y) / Scale;

    public bool Locked => layout.Locked;
    public float Scale => (float)layout.Scale;

    public HudManager(Node parent, Window window, string savePath)
    {
        this.window = window; this.savePath = savePath;
        var layer = new CanvasLayer { Layer = 5 };
        Root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(Root);
        parent.AddChild(layer);
        Load();
        ApplyScale();
        window.SizeChanged += () => Callable.From(RepositionAll).CallDeferred();
        BuildTopBar();
        BuildInterfaceWindow();
    }

    public HudWindow Get(string id) => windows.TryGetValue(id, out var w) ? w : null;

    /// <summary>Add a window. posFrac = default top-left as a fraction of the screen (negative = measured from the right/bottom edge).</summary>
    public HudWindow Add(string id, string title, Control content, Vector2 posFrac, Vector2 size, bool resizable = true, bool visibleByDefault = true)
    {
        var w = new HudWindow(id, title, content) { Resizable = resizable, TopLimit = () => TopInset };
        w.Changed += Save;
        Root.AddChild(w);
        windows[id] = w;
        defaults[id] = (posFrac, size); defaultVisible[id] = visibleByDefault;
        w.SetLocked(layout.Locked);
        PlaceFromDefaultOrSaved(w, visibleByDefault);
        return w;
    }

    void PlaceFromDefaultOrSaved(HudWindow w, bool visibleByDefault)
    {
        var ps = UiSize;
        if (layout.Windows.TryGetValue(w.WindowId, out var st))
        {
            w.Size = FitSize(new Vector2((float)st.W, (float)st.H), ps);
            w.Position = Clamp(new Vector2((float)(st.X * ps.X), (float)(st.Y * ps.Y)), w.Size, ps);
            w.Visible = st.Visible;
        }
        else
        {
            var (pf, sz) = defaults[w.WindowId];
            float x = pf.X >= 0 ? pf.X * ps.X : ps.X + pf.X * ps.X - sz.X;
            float y = pf.Y >= 0 ? pf.Y * ps.Y : ps.Y + pf.Y * ps.Y - sz.Y;
            w.Size = FitSize(sz, ps);
            w.Position = Clamp(new Vector2(x, y), w.Size, ps);
            w.Visible = visibleByDefault;
        }
    }

    /// <summary>Height of the top bar in UI units; windows may not cover it.</summary>
    public float TopInset { get; private set; } = 0f;

    /// <summary>Keep a window completely inside the client area and below the top bar.</summary>
    Vector2 Clamp(Vector2 pos, Vector2 size, Vector2 area)
    {
        float top = TopInset;
        float maxX = Mathf.Max(0, area.X - size.X), maxY = Mathf.Max(top, area.Y - size.Y);
        return new Vector2(Mathf.Clamp(pos.X, 0, maxX), Mathf.Clamp(pos.Y, top, maxY));
    }

    /// <summary>A window never gets bigger than the area it lives in.</summary>
    Vector2 FitSize(Vector2 size, Vector2 area) => new Vector2(Mathf.Min(size.X, area.X), Mathf.Min(size.Y, Mathf.Max(60, area.Y - TopInset)));

    /// <summary>Re-place every window from its saved screen fraction (after a window resize or UI-scale change).</summary>
    void RepositionAll()
    {
        var ps = UiSize;
        foreach (var w in windows.Values)
        {
            w.Size = FitSize(w.Size, ps);
            if (layout.Windows.TryGetValue(w.WindowId, out var st))
                w.Position = Clamp(new Vector2((float)(st.X * ps.X), (float)(st.Y * ps.Y)), w.Size, ps);
            else w.Position = Clamp(w.Position, w.Size, ps);
        }
    }

    public void ResetLayout()
    {
        layout.Windows.Clear();
        foreach (var w in windows.Values)
            if (w.WindowId != "interface") PlaceFromDefaultOrSaved(w, defaultVisible.GetValueOrDefault(w.WindowId, true));
        Save();
    }

    public void Toggle(string id) { if (windows.TryGetValue(id, out var w)) { w.Visible = !w.Visible; if (w.Visible) w.GetParent().MoveChild(w, -1); Save(); } }

    public void SetLocked(bool locked)
    {
        layout.Locked = locked;
        foreach (var w in windows.Values) w.SetLocked(locked);
        Save();
    }

    public void SetScale(float s)
    {
        Save();            // store fractions under the OLD scale first, then rescale and re-place
        layout.Scale = Math.Clamp(s, 0.6, 2.5);
        ApplyScale();
        // the new visible rect is only known after the window has processed the scale change
        Callable.From(() => { RepositionAll(); Save(); }).CallDeferred();
    }

    void ApplyScale()
    {
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleSize = Vector2I.Zero;       // no stretching to a base resolution: a bigger window = more room, not bigger UI
        window.ContentScaleFactor = (float)layout.Scale;
    }

    // ------------------------------------------------------------ persistence
    public void Save()
    {
        try
        {
            var ps = UiSize;
            foreach (var w in windows.Values)
                layout.Windows[w.WindowId] = new WinState { X = w.Position.X / ps.X, Y = w.Position.Y / ps.Y, W = w.Size.X, H = w.Size.Y, Visible = w.Visible };
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            File.WriteAllText(savePath, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { GD.PrintErr("HUD save failed: " + ex.Message); }
    }

    void Load()
    {
        try
        {
            if (File.Exists(savePath))
                layout = JsonSerializer.Deserialize<LayoutFile>(File.ReadAllText(savePath)) ?? new LayoutFile();
        }
        catch (Exception ex) { GD.PrintErr("HUD layout unreadable, using defaults: " + ex.Message); layout = new LayoutFile(); }
    }

    // ------------------------------------------------------------ "Interface" settings window + gear button
    /// <summary>Content of the Interface window; other systems append their settings here.</summary>
    public VBoxContainer InterfaceBox;

    void BuildInterfaceWindow()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        lockCheck = new CheckButton { Text = "Lock windows", ButtonPressed = layout.Locked };
        lockCheck.Toggled += on => { if (!suppressUi) SetLocked(on); };
        scaleLabel = new Label { Text = $"UI scale: {layout.Scale:F2}" };
        scaleSlider = new HSlider { MinValue = 0.6, MaxValue = 2.0, Step = 0.05, Value = layout.Scale, CustomMinimumSize = new Vector2(180, 20) };
        // apply after a short pause so the mouse wheel and keyboard work too (applying live while dragging would make the slider run away)
        var debounce = new Timer { OneShot = true, WaitTime = 0.35 };
        Root.AddChild(debounce);
        debounce.Timeout += () => SetScale((float)scaleSlider.Value);
        scaleSlider.ValueChanged += v => { if (!suppressUi) { scaleLabel.Text = $"UI scale: {v:F2}"; debounce.Start(); } };
        var reset = new Button { Text = "Reset window layout" };
        reset.Pressed += ResetLayout;
        box.AddChild(lockCheck); box.AddChild(scaleLabel); box.AddChild(scaleSlider); box.AddChild(reset);
        InterfaceBox = box;
        Add("interface", "Interface", box, new Vector2(-0.02f, 0.06f), new Vector2(260, 350), resizable: false, visibleByDefault: false);
    }

    // ------------------------------------------------------------ top bar (rest button, window buttons, menu)
    HBoxContainer barLeft, barRight; Control barPanel;
    const float BarHeight = 38f;

    void BuildTopBar()
    {
        TopInset = BarHeight;
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.10f, 0.96f), BorderColor = new Color(0.40f, 0.40f, 0.52f, 0.95f),
            BorderWidthBottom = 1, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2,
        });
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        panel.OffsetBottom = BarHeight;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
        barLeft = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; barLeft.AddThemeConstantOverride("separation", 6);
        barRight = new HBoxContainer(); barRight.AddThemeConstantOverride("separation", 6);
        row.AddChild(barLeft); row.AddChild(barRight);
        panel.AddChild(row);
        Root.AddChild(panel); Root.MoveChild(panel, 0);
        barPanel = panel;
    }

    /// <summary>True if the (viewport) point is over the top bar.</summary>
    public bool OverBar(Vector2 p) => barPanel != null && barPanel.GetGlobalRect().HasPoint(p);

    /// <summary>Add a text button to the top bar (left group, or the right edge).</summary>
    public Button AddBarButton(string text, string tooltip, Action pressed, bool right = false)
    {
        var b = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
        b.Pressed += pressed;
        (right ? barRight : barLeft).AddChild(b);
        return b;
    }

    /// <summary>Add an icon button (two-frame sprite sheet from the original client: normal | pressed).</summary>
    public TextureButton AddBarIcon(Texture2D normal, Texture2D pressed, string tooltip, Vector2 size, Action onPressed)
    {
        var b = new TextureButton
        {
            TextureNormal = normal, TexturePressed = pressed, TextureHover = normal, IgnoreTextureSize = true, StretchMode = TextureButton.StretchModeEnum.Scale,
            CustomMinimumSize = size, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        b.Pressed += onPressed;
        barLeft.AddChild(b);
        return b;
    }

    public void AddBarSeparator() => barLeft.AddChild(new VSeparator());
}
