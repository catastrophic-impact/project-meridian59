using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Files.Room;

/// <summary>
/// Automap like the original: walls appear once they have been seen (or are flagged "always"), never if flagged "never".
/// North is up; the player is an arrow; nearby objects are dots. Mouse wheel zooms.
/// </summary>
public partial class MiniMap : Control
{
    const uint WF_TRANSPARENT = 0x2, WF_MAP_NEVER = 0x8, WF_MAP_ALWAYS = 0x10;
    const float ObjectRange = 7 * 1024f;        // original MAP_OBJECT_DISTANCE
    const float SeeRange = 24 * 1024f;

    public Func<float> PlayerX, PlayerY, Facing;                       // fineness; facing = world direction angle (radians, 0 = east)
    public Func<IEnumerable<(float x, float y, int kind)>> Objects;    // kind 0 other, 1 attackable, 2 player

    RoomFile room;
    bool[] seen;
    bool[] drawable;                  // wall has something worth showing on a map
    readonly Dictionary<string, bool[]> seenByRoom = new Dictionary<string, bool[]>();
    string roomKey;
    float roomCx, roomCy;
    float CenterX => Maximized ? roomCx : PlayerX();
    float CenterY => Maximized ? roomCy : PlayerY();   // full-screen view shows the whole room; the small map follows the player
    float roomW = 1024f, roomH = 1024f;
    float zoom = 1f;                  // pixels per 1024 fineness = 10 * zoom
    double nextSee;
    static readonly bool ShowAll = System.Environment.GetEnvironmentVariable("M59_MAPALL") == "1";

    // ---- persistence (explored walls + notes per room), like the original game.map
    public string SavePath;
    public Action<bool> ToggleMax;                  // called with the wanted state when the corner button is clicked
    public bool Maximized;
    class Note { public float X, Y; public string Text = ""; }
    class RoomMem { public string Seen = ""; public List<Note> Notes = new List<Note>(); }
    Dictionary<string, RoomMem> mem;
    List<Note> notes = new List<Note>();
    bool dirty; double nextSave;
    float fitScale = 10f / 1024f;                   // pixels per fineness that fits the whole room (maximized view)
    Texture2D bkgnd, noteTex;
    LineEdit noteEdit; Note editing; bool editingNew; float editX, editY;
    const int MaxNotes = 20, MaxNoteLen = 99;
    Rect2 maxButton => new Rect2(Size.X - 22, 4, 18, 18);

    public MiniMap()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; CustomMinimumSize = new Vector2(100, 100);
        try
        {
            bkgnd = ImageTexture.CreateFromImage(Image.LoadFromFile(ProjectSettings.GlobalizePath("res://ui/map_bkgnd.png")));
            noteTex = ImageTexture.CreateFromImage(Image.LoadFromFile(ProjectSettings.GlobalizePath("res://ui/map_note.png")));
        }
        catch { }
        noteEdit = new LineEdit { Visible = false, MaxLength = MaxNoteLen, PlaceholderText = "Note (empty = delete, Esc = cancel)", CustomMinimumSize = new Vector2(240, 0) };
        noteEdit.TextSubmitted += CommitNote;
        noteEdit.GuiInput += e => { if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape) { CancelNote(); noteEdit.AcceptEvent(); } };
        AddChild(noteEdit);
    }

    void LoadMem()
    {
        if (mem != null) return;
        mem = new Dictionary<string, RoomMem>();
        try
        {
            if (SavePath != null && System.IO.File.Exists(SavePath))
                mem = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, RoomMem>>(System.IO.File.ReadAllText(SavePath),
                    new System.Text.Json.JsonSerializerOptions { IncludeFields = true }) ?? mem;
        }
        catch { }
    }

    string PackSeen() => seen == null ? "" : Convert.ToBase64String(PackBits(seen));
    static byte[] PackBits(bool[] b) { var r = new byte[(b.Length + 7) / 8]; for (int i = 0; i < b.Length; i++) if (b[i]) r[i / 8] |= (byte)(1 << (i % 8)); return r; }

    /// <summary>Write the current room's explored walls and notes (and everything saved before) to disk.</summary>
    public void Save()
    {
        if (SavePath == null || roomKey == null || seen == null) return;
        LoadMem();
        mem[roomKey] = new RoomMem { Seen = PackSeen(), Notes = notes };
        try { System.IO.File.WriteAllText(SavePath, System.Text.Json.JsonSerializer.Serialize(mem, new System.Text.Json.JsonSerializerOptions { IncludeFields = true })); } catch { }
        dirty = false;
    }

    public void SetRoom(RoomFile r, string key)
    {
        if (room != null && dirty) Save();
        CancelNote();
        room = r; roomKey = key;
        if (r == null) { seen = null; drawable = null; return; }
        LoadMem();
        if (!seenByRoom.TryGetValue(key, out seen) || seen.Length != r.Walls.Count)
        {
            seenByRoom[key] = seen = new bool[r.Walls.Count];
            if (mem.TryGetValue(key, out var rm) && !string.IsNullOrEmpty(rm.Seen))
            {
                try { var bytes = Convert.FromBase64String(rm.Seen); if (bytes.Length == (seen.Length + 7) / 8) for (int i = 0; i < seen.Length; i++) seen[i] = (bytes[i / 8] & (1 << (i % 8))) != 0; } catch { }
            }
        }
        notes = mem.TryGetValue(key, out var rm2) && rm2.Notes != null ? rm2.Notes : new List<Note>();
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var w in r.Walls) { x0 = Math.Min(x0, Math.Min(w.X0, w.X1)); x1 = Math.Max(x1, Math.Max(w.X0, w.X1)); y0 = Math.Min(y0, Math.Min(w.Y0, w.Y1)); y1 = Math.Max(y1, Math.Max(w.Y0, w.Y1)); }
        roomW = Math.Max(1024f, x1 - x0); roomH = Math.Max(1024f, y1 - y0); roomCx = (x0 + x1) / 2; roomCy = (y0 + y1) / 2;
        drawable = new bool[r.Walls.Count];
        for (int i = 0; i < r.Walls.Count; i++) drawable[i] = IsMappable(r.Walls[i]);
        nextSee = 0; QueueRedraw();
    }

    RoomSideDef Sd(int n) => n > 0 && n <= room.SideDefs.Count ? room.SideDefs[n - 1] : null;
    RoomSector Sec(int n) => n > 0 && n <= room.Sectors.Count ? room.Sectors[n - 1] : null;

    bool IsMappable(RoomWall w)
    {
        var pos = Sd(w.PosSideDef); var neg = Sd(w.NegSideDef);
        var sd = pos ?? neg;
        if (sd == null) return false;
        if ((sd.Flags & WF_MAP_NEVER) != 0) return false;
        if ((sd.Flags & WF_MAP_ALWAYS) != 0) return true;
        var a = Sec(w.PosSector); var b = Sec(w.NegSector);
        if (a == null || b == null) return true;                               // edge of the world: solid
        if (a.FloorHeight != b.FloorHeight || a.CeilingHeight != b.CeilingHeight) return true;   // steps, ledges, doorways with a frame
        return (pos != null && pos.NormalBmp != 0 && (pos.Flags & WF_TRANSPARENT) == 0) || (neg != null && neg.NormalBmp != 0 && (neg.Flags & WF_TRANSPARENT) == 0);
    }

    bool Blocks(RoomWall w)
    {
        var a = Sec(w.PosSector); var b = Sec(w.NegSector);
        if (a == null || b == null) return true;
        foreach (var sd in new[] { Sd(w.PosSideDef), Sd(w.NegSideDef) })
            if (sd != null && sd.NormalBmp != 0 && (sd.Flags & WF_TRANSPARENT) == 0) return true;
        return false;
    }

    static bool Cross(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
    {
        float d1x = bx - ax, d1y = by - ay, d2x = dx - cx, d2y = dy - cy;
        float den = d1x * d2y - d1y * d2x;
        if (Math.Abs(den) < 1e-6f) return false;
        float t = ((cx - ax) * d2y - (cy - ay) * d2x) / den;
        float u = ((cx - ax) * d1y - (cy - ay) * d1x) / den;
        return t > 0.001f && t < 0.999f && u > 0.001f && u < 0.999f;
    }

    /// <summary>Mark walls in front of the player (inside the view cone, in range, not hidden behind a solid wall) as seen.</summary>
    void UpdateSeen(float px, float py, float facing)
    {
        if (room == null) return;
        float fx = Mathf.Cos(facing), fy = Mathf.Sin(facing);
        for (int i = 0; i < room.Walls.Count; i++)
        {
            if (seen[i] || !drawable[i]) continue;
            var w = room.Walls[i];
            float mx = (w.X0 + w.X1) / 2f, my = (w.Y0 + w.Y1) / 2f;
            float dx = mx - px, dy = my - py, d2 = dx * dx + dy * dy;
            if (d2 > SeeRange * SeeRange) continue;
            // in front (about +-60 degrees) unless very close
            if (d2 > 256f * 256f && (dx * fx + dy * fy) < Mathf.Sqrt(d2) * 0.5f) continue;
            bool blocked = false;
            for (int j = 0; j < room.Walls.Count && !blocked; j++)
            {
                if (j == i) continue;
                var o = room.Walls[j];
                if (Blocks(o) && Cross(px, py, mx, my, o.X0, o.Y0, o.X1, o.Y1)) blocked = true;
            }
            if (!blocked) seen[i] = true;
        }
    }

    public override void _ExitTree() { if (dirty) Save(); }

    public override void _Process(double delta)
    {
        if (room == null || PlayerX == null || !IsVisibleInTree()) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now >= nextSee) { nextSee = now + 0.25; UpdateSeen(PlayerX(), PlayerY(), Facing()); dirty = true; }
        if (dirty && now >= nextSave) { nextSave = now + 30; Save(); }
        QueueRedraw();
    }

    float Scale => (Maximized ? fitScale : 10f / 1024f) * zoom;

    Vector2 WorldAt(Vector2 m) => new Vector2(CenterX + (m.X - Size.X / 2) / Scale, CenterY + (m.Y - Size.Y / 2) / Scale);

    Note NoteNear(Vector2 m)
    {
        foreach (var n in notes)
        {
            var p = new Vector2(Size.X / 2 + (n.X - CenterX) * Scale, Size.Y / 2 + (n.Y - CenterY) * Scale);
            if (Math.Abs(p.X - m.X) <= 12 && Math.Abs(p.Y - m.Y) <= 12) return n;
        }
        return null;
    }

    void CancelNote() { if (noteEdit != null) noteEdit.Visible = false; editing = null; }

    void CommitNote(string text)
    {
        text = (text ?? "").Trim();
        if (editing != null)
        {
            if (text.Length == 0) notes.Remove(editing);
            else { editing.Text = text; if (editingNew) { editing.X = editX; editing.Y = editY; notes.Add(editing); } }
            dirty = true; Save();
        }
        CancelNote();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { zoom = Mathf.Min(8f, zoom * 1.15f); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown) { zoom = Mathf.Max(0.4f, zoom / 1.15f); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.Left && room != null && PlayerX != null)
            {
                AcceptEvent();
                if (maxButton.HasPoint(mb.Position)) { ToggleMax?.Invoke(!Maximized); return; }
                // like the original: click on a note to edit it, click anywhere else to add one
                var hit = NoteNear(mb.Position);
                if (hit == null && notes.Count >= MaxNotes) return;
                var w = WorldAt(mb.Position);
                editing = hit ?? new Note(); editingNew = hit == null; editX = w.X; editY = w.Y;
                noteEdit.Text = editing.Text;
                noteEdit.Position = new Vector2(Mathf.Clamp(mb.Position.X - 120, 4, Math.Max(4, Size.X - 250)), Mathf.Clamp(mb.Position.Y + 10, 4, Math.Max(4, Size.Y - 32)));
                noteEdit.Visible = true; noteEdit.GrabFocus();
            }
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.6f, 0.6f, 0.6f));
        if (room == null || PlayerX == null) return;
        float px = CenterX, py = CenterY;
        fitScale = Math.Min(Size.X / roomW, Size.Y / roomH) * 0.95f;
        float s = Scale;
        if (bkgnd != null)                                   // the grey noise scrolls with the player, like the original
        {
            var ts = bkgnd.GetSize();
            float ox = -((px * s) % ts.X + ts.X) % ts.X, oy = -((py * s) % ts.Y + ts.Y) % ts.Y;
            for (float y = oy - ts.Y; y < Size.Y; y += ts.Y) for (float x = ox - ts.X; x < Size.X; x += ts.X) DrawTexture(bkgnd, new Vector2(x, y));
        }
        var c = Size / 2;
        Vector2 M(float wx, float wy) => new Vector2(c.X + (wx - px) * s, c.Y + (wy - py) * s);   // world y points south = screen down, north is up

        for (int i = 0; i < room.Walls.Count; i++)
        {
            if (!drawable[i]) continue;
            var w = room.Walls[i];
            var sd = Sd(w.PosSideDef) ?? Sd(w.NegSideDef);
            bool always = sd != null && (sd.Flags & WF_MAP_ALWAYS) != 0;
            if (!seen[i] && !always && !ShowAll) continue;
            var a = Sec(w.PosSector); var b = Sec(w.NegSector);
            bool solid = a == null || b == null || Blocks(w);
            DrawLine(M(w.X0, w.Y0), M(w.X1, w.Y1), solid ? Colors.Black : new Color(0.25f, 0.25f, 0.28f), solid ? 1.6f : 1f);
        }

        // notes: blue squares with the text under them (the text is hidden when zoomed far out)
        if (noteTex != null)
        {
            int size = Math.Max(14, Math.Min(30, (int)(2 * 1024 * s)));
            var font = ThemeDB.FallbackFont;
            foreach (var n in notes)
            {
                var p = M(n.X, n.Y);
                DrawTextureRect(noteTex, new Rect2(p - new Vector2(size, size) / 2, new Vector2(size, size)), false);
                if (Maximized || s > 0.012f)
                {
                    var tp = new Vector2(p.X - font.GetStringSize(n.Text, HorizontalAlignment.Left, -1, 13).X / 2, p.Y + size / 2 + 13);
                    DrawString(font, tp + new Vector2(1, 1), n.Text, HorizontalAlignment.Left, -1, 13, Colors.Black);
                    DrawString(font, tp, n.Text, HorizontalAlignment.Left, -1, 13, Colors.White);
                }
            }
        }

        if (Objects != null)
            foreach (var (ox, oy, kind) in Objects())
            {
                float dx = ox - PlayerX(), dy = oy - PlayerY();
                if (dx * dx + dy * dy > ObjectRange * ObjectRange) continue;
                var col = kind == 2 ? new Color(0f, 0.2f, 1f) : kind == 3 ? new Color(0f, 0.65f, 0f) : kind == 1 ? new Color(1f, 0f, 0f) : new Color(0.55f, 0.4f, 0f);
                DrawCircle(M(ox, oy), kind == 0 ? 2f : 3f, col);
            }

        // player arrow
        float f = Facing();
        var pc = M(PlayerX(), PlayerY());
        var dir = new Vector2(Mathf.Cos(f), Mathf.Sin(f));
        var side = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { pc + dir * 7, pc - dir * 5 + side * 4.5f, pc - dir * 2, pc - dir * 5 - side * 4.5f }, new Color(0f, 0f, 1f));

        // maximize / restore button (up arrow / down arrow)
        var br = maxButton;
        DrawRect(br, new Color(0.1f, 0.1f, 0.15f, 0.85f)); DrawRect(br, new Color(0.8f, 0.8f, 0.9f), false, 1f);
        var bc = br.GetCenter();
        float dy2 = Maximized ? 4f : -4f;
        DrawColoredPolygon(new[] { bc + new Vector2(0, dy2), bc + new Vector2(-5, -dy2), bc + new Vector2(5, -dy2) }, Colors.White);
    }
}
