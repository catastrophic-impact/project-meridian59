using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>Enchantment bar (buffs and debuffs) centred under the top bar, one row of icons like WoW. Each icon shows the time left underneath
/// (from our server extension; enchantments without a timer show nothing) and the full name + time in its tooltip. Harmful ones have a red frame.</summary>
public partial class Main
{
    HBoxContainer buffRow; double buffNextRefresh; bool buffsDirty = true;

    partial class BuffIcon : Control
    {
        public uint Id; public bool Harmful, Room; public Texture2D Icon; public string Name = ""; public Func<long?> Remaining; public Label Time;
        public BuffIcon() { CustomMinimumSize = new Vector2(36, 50); MouseFilter = MouseFilterEnum.Stop; }
        public override void _Draw()
        {
            var r = new Rect2(2, 2, 32, 32);
            DrawRect(new Rect2(r.Position - new Vector2(2, 2), r.Size + new Vector2(4, 4)), Harmful ? new Color(0.8f, 0.1f, 0.1f) : Room ? new Color(0.35f, 0.5f, 0.9f) : new Color(0.1f, 0.1f, 0.12f, 0.9f));
            if (Icon != null) DrawTextureRect(Icon, r, false); else DrawRect(r, new Color(0.3f, 0.3f, 0.3f));
        }
        public override string _GetTooltip(Vector2 atPosition)
        {
            long? ms = Remaining?.Invoke();
            return ms == null ? Name : Name + "  (" + FormatTime(ms.Value) + ")";
        }
    }

    static string FormatTime(long ms)
    {
        long s = (ms + 999) / 1000;
        if (s >= 3600) return $"{s / 3600}h {s % 3600 / 60:00}m";
        if (s >= 60) return $"{s / 60}:{s % 60:00}";
        return s + "s";
    }

    void BuildBuffBar()
    {
        var layer = new CanvasLayer { Layer = 3 };
        buffRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        buffRow.AddThemeConstantOverride("separation", 4);
        buffRow.Alignment = BoxContainer.AlignmentMode.Center;
        buffRow.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        buffRow.AnchorLeft = 0f; buffRow.AnchorRight = 1f; buffRow.OffsetTop = 40; buffRow.OffsetBottom = 94;
        layer.AddChild(buffRow); AddChild(layer);
        client.BuffsChanged += () => buffsDirty = true;
        client.CombatNumberReceived += OnCombatNumber;
    }

    void UpdateBuffBar()
    {
        if (buffRow == null || !buffRow.Visible) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (buffsDirty)
        {
            buffsDirty = false;
            foreach (var c in buffRow.GetChildren()) c.QueueFree();
            void add(ObjectBase b, bool room)
            {
                client.BuffTimes.TryGetValue(b.ID, out var bt);
                var icon = new BuffIcon { Id = b.ID, Room = room, Harmful = bt != null && bt.Harmful, Name = client.Resolve(b.NameRID) ?? "?", Icon = IconFor(b) };
                icon.Remaining = () => room || bt == null || !bt.Timed ? (long?)null : Math.Max(0, bt.ExpiresAt - System.Environment.TickCount64);
                icon.Time = new Label { Position = new Vector2(0, 34), Size = new Vector2(36, 16), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
                icon.Time.AddThemeFontSizeOverride("font_size", 12);
                icon.Time.AddThemeColorOverride("font_outline_color", Colors.Black); icon.Time.AddThemeConstantOverride("outline_size", 4);
                icon.AddChild(icon.Time);
                buffRow.AddChild(icon);
            }
            foreach (var b in client.AvatarBuffs) add(b, false);
            foreach (var b in client.RoomBuffs) add(b, true);
            buffNextRefresh = 0;
        }
        if (now < buffNextRefresh) return;
        buffNextRefresh = now + 0.25;
        foreach (var c in buffRow.GetChildren())
            if (c is BuffIcon bi && bi.Time != null)
            {
                long? ms = bi.Remaining?.Invoke();
                bi.Time.Text = ms == null ? "" : FormatTime(ms.Value);
                bi.Time.Modulate = ms != null && ms < 10000 ? new Color(1f, 0.5f, 0.4f) : Colors.White;
            }
    }
}
