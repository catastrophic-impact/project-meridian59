using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>One inventory cell: icon, stack count, highlight when equipped. Supports drag (to drop / hotbar later).</summary>
public partial class InvSlot : Control
{
    public uint ItemId;
    public string ItemName = "";
    public bool Equipped;
    public bool Selected;
    public Texture2D Icon;
    public int Count;
    public Action<InvSlot> Activated;        // double click / right click
    public Action<InvSlot> Clicked;
    public Action<InvSlot> RightClicked;     // look
    public string Kind = "item";             // what dragging this slot carries: item / spell / skill / attack
    public string Label = "";                // text shown when there is no icon (e.g. "Attack")
    public string Hotkey = "";               // small number in the corner (hotbar)
    public bool AcceptDrops;                 // hotbar slots accept dropped items / spells / skills
    public Action<InvSlot, Godot.Collections.Dictionary> Dropped;
    static readonly Color Bg = new Color(0.10f, 0.10f, 0.14f, 0.9f);

    public InvSlot() { CustomMinimumSize = new Vector2(46, 46); MouseFilter = MouseFilterEnum.Stop; }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Bg);
        if (Icon != null)
        {
            float k = Mathf.Min((Size.X - 6) / Icon.GetWidth(), (Size.Y - 6) / Icon.GetHeight());
            var sz = new Vector2(Icon.GetWidth(), Icon.GetHeight()) * k;
            DrawTextureRect(Icon, new Rect2((Size - sz) / 2, sz), false);
        }
        if (Icon == null && Label != "")
            DrawString(ThemeDB.FallbackFont, new Vector2(2, Size.Y / 2 + 5), Label, HorizontalAlignment.Center, Size.X - 4, 12, Colors.White);
        if (Hotkey != "")
            DrawString(ThemeDB.FallbackFont, new Vector2(4, 13), Hotkey, HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.6f));
        var border = Equipped ? new Color(1f, 0.85f, 0.2f) : (Selected ? new Color(0.6f, 0.8f, 1f) : new Color(0.35f, 0.35f, 0.45f));
        DrawRect(r, border, false, Equipped ? 3 : (Selected ? 2 : 1));
        if (Count > 1)
        {
            var f = ThemeDB.FallbackFont;
            DrawString(f, new Vector2(Size.X - 28, Size.Y - 4), Count.ToString(), HorizontalAlignment.Right, 24, 12, Colors.White);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Right) RightClicked?.Invoke(this);
            else if (mb.ButtonIndex == MouseButton.Left && mb.DoubleClick) Activated?.Invoke(this);
            else if (mb.ButtonIndex == MouseButton.Left) Clicked?.Invoke(this);
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!AcceptDrops || data.VariantType != Variant.Type.Dictionary) return false;
        return data.AsGodotDictionary().ContainsKey("kind");
    }

    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(this, data.AsGodotDictionary());

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (ItemId == 0 && !Main.IsCommandKind(Kind)) return default;
        var preview = new Control();
        var tr = new TextureRect { Texture = Icon, CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Position = new Vector2(-20, -20) };
        preview.AddChild(tr);
        SetDragPreview(preview);
        var d = new Godot.Collections.Dictionary { { "kind", Kind }, { "id", (long)ItemId }, { "name", ItemName }, { "count", (long)Count } };
        return d;
    }
}

/// <summary>Invisible full-screen area behind the HUD: dropping an item on the 3D view drops it on the floor.</summary>
public partial class WorldDropTarget : Control
{
    public Action<uint, uint> ItemDropped;
    public WorldDropTarget() { MouseFilter = MouseFilterEnum.Stop; SetAnchorsPreset(LayoutPreset.FullRect); }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        return d.ContainsKey("kind") && (string)d["kind"] == "item";
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        ItemDropped?.Invoke((uint)(long)d["id"], d.ContainsKey("count") ? (uint)(long)d["count"] : 0u);
    }
}

/// <summary>One row of the Spells / Skills lists: small icon on the left, the name next to it; hover shows a description, double-click activates, drag onto the hotbar.</summary>
public partial class ListRow : Control
{
    public uint ItemId;
    public string ItemName = "";
    public Texture2D Icon;
    public string Kind = "spell";
    public Color NameColor = Colors.White;
    public Action<ListRow> Activated;        // double click
    public Action<ListRow> RightClicked;     // look
    public Action<ListRow> Hovered;          // first hover (used to fetch the description)
    bool hover;

    public ListRow()
    {
        CustomMinimumSize = new Vector2(0, 34);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { hover = true; QueueRedraw(); Hovered?.Invoke(this); };
        MouseExited += () => { hover = false; QueueRedraw(); };
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, hover ? new Color(0.22f, 0.24f, 0.34f, 0.95f) : new Color(0.10f, 0.10f, 0.14f, 0.85f));
        DrawRect(r, new Color(0.30f, 0.30f, 0.40f), false, 1);
        float x = 4;
        if (Icon != null)
        {
            float k = Mathf.Min(26f / Icon.GetWidth(), 26f / Icon.GetHeight());
            var sz = new Vector2(Icon.GetWidth(), Icon.GetHeight()) * k;
            DrawTextureRect(Icon, new Rect2(new Vector2(4, (Size.Y - sz.Y) / 2), sz), false);
        }
        x = 38;
        DrawString(ThemeDB.FallbackFont, new Vector2(x, Size.Y / 2 + 5), ItemName, HorizontalAlignment.Left, Size.X - x - 4, 14, NameColor);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Right) RightClicked?.Invoke(this);
            else if (mb.ButtonIndex == MouseButton.Left && mb.DoubleClick) Activated?.Invoke(this);
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (ItemId == 0 && !Main.IsCommandKind(Kind)) return default;
        var preview = new Control();
        preview.AddChild(new TextureRect { Texture = Icon, CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Position = new Vector2(-20, -20) });
        SetDragPreview(preview);
        return new Godot.Collections.Dictionary { { "kind", Kind }, { "id", (long)ItemId }, { "name", ItemName }, { "count", 0L } };
    }
}
