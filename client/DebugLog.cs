using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

/// <summary>Optional debug log (Interface window, F10): one file, userdata/client_debug.log, overwritten every time the client starts. Switched on per topic
/// ("Companions", "Network") so it can be reused for different tests; off = no file is touched. Env M59_DEBUGLOG=companions,net also switches topics on.</summary>
public partial class Main
{
    class DebugSettings { public bool Enabled { get; set; } public bool Companions { get; set; } = true; public bool Network { get; set; } public bool Movement { get; set; } }
    DebugSettings dbg = new DebugSettings();
    string DebugSettingsPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/debuglog.json"));
    string DebugLogPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/client_debug.log"));
    StreamWriter dbgWriter; double dbgNext;
    readonly Dictionary<uint, (float x, float y, double still)> dbgLast = new Dictionary<uint, (float, float, double)>();

    void LoadDebugSettings()
    {
        try { if (File.Exists(DebugSettingsPath)) dbg = JsonSerializer.Deserialize<DebugSettings>(File.ReadAllText(DebugSettingsPath)) ?? dbg; } catch { }
        var env = System.Environment.GetEnvironmentVariable("M59_DEBUGLOG");
        if (!string.IsNullOrEmpty(env)) { dbg.Enabled = true; dbg.Companions = env.Contains("companion"); dbg.Network = env.Contains("net"); dbg.Movement = env.Contains("move"); }
        if (dbg.Enabled) OpenDebugLog();
    }

    void SaveDebugSettings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(DebugSettingsPath)); File.WriteAllText(DebugSettingsPath, JsonSerializer.Serialize(dbg)); } catch { }
    }

    void OpenDebugLog()
    {
        if (dbgWriter != null) return;
        try
        {
            dbgWriter = new StreamWriter(new FileStream(DebugLogPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            dbgWriter.WriteLine($"# M59 client debug log {DateTime.Now:yyyy-MM-dd HH:mm:ss}  (overwritten at every start)  topics: companions={dbg.Companions} net={dbg.Network}");
        }
        catch { dbgWriter = null; }
    }

    /// <summary>Write a line for a topic ("companions", "net") if the log and that topic are on.</summary>
    void Dbg(string topic, string msg)
    {
        if (!dbg.Enabled || dbgWriter == null) return;
        if (topic == "companions" && !dbg.Companions) return;
        if (topic == "net" && !dbg.Network) return;
        if (topic == "move" && !dbg.Movement) return;   // topic "mark" is always written while the log is on
        try { dbgWriter.WriteLine($"{Time.GetTicksMsec() / 1000.0,8:F2} [{topic}] {msg}"); } catch { }
    }

    void BuildDebugLogUi()
    {
        var box = hudMgr?.InterfaceBox; if (box == null) return;
        var on = new CheckButton { Text = "Debug log", ButtonPressed = dbg.Enabled, TooltipText = "Writes what the client sees to userdata/client_debug.log, overwritten every time the client starts. Topics: companions (positions, stuck), network (messages), movement (server reports vs drawn position, wall crossings)." };
        var comp = new CheckButton { Text = "  companions", ButtonPressed = dbg.Companions };
        var net = new CheckButton { Text = "  network", ButtonPressed = dbg.Network };
        on.Toggled += v => { dbg.Enabled = v; if (v) OpenDebugLog(); SaveDebugSettings(); };
        comp.Toggled += v => { dbg.Companions = v; SaveDebugSettings(); };
        net.Toggled += v => { dbg.Network = v; SaveDebugSettings(); };
        var mv = new CheckButton { Text = "  movement", ButtonPressed = dbg.Movement };
        mv.Toggled += v => { dbg.Movement = v; SaveDebugSettings(); };
        box.AddChild(on); box.AddChild(comp); box.AddChild(net); box.AddChild(mv);
        client.Log += s => Dbg("net", s);
    }

    /// <summary>Twice a second: for each of our companions the distance to us, how fast it moved, and a STUCK flag when it stands still while far away.</summary>
    void UpdateDebugLog()
    {
        if (!dbg.Enabled || !dbg.Companions || !roomShown || walker == null) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < dbgNext) return;
        double dt = now - (dbgNext - 0.5 > 0 ? dbgNext - 0.5 : now - 0.5);
        dbgNext = now + 0.5;
        foreach (var m in client.Group)
        {
            if (m.Kind != 2) continue;
            if (!objPos.TryGetValue(m.Id, out var p)) { Dbg("companions", $"{m.Name} #{m.Id} (not in this room)  hp {m.Hp}/{m.MaxHp}"); continue; }
            float dist = Mathf.Sqrt((p.x - walker.X) * (p.x - walker.X) + (p.y - walker.Y) * (p.y - walker.Y)) / 1024f;
            double still = 0; float speed = 0;
            if (dbgLast.TryGetValue(m.Id, out var l))
            {
                float moved = Mathf.Sqrt((p.x - l.x) * (p.x - l.x) + (p.y - l.y) * (p.y - l.y)) / 1024f;
                speed = moved / (float)Math.Max(0.1, dt);
                still = moved < 0.05f ? l.still + dt : 0;
            }
            dbgLast[m.Id] = (p.x, p.y, still);
            string flag = dist > 5f && still >= 2 ? "  ** STUCK **" : "";
            Dbg("companions", $"{m.Name} #{m.Id} role {m.Role} pos ({p.x / 1024f:F1},{p.y / 1024f:F1}) player ({walker.X / 1024f:F1},{walker.Y / 1024f:F1}) dist {dist:F1} sq speed {speed:F1} sq/s still {still:F1}s hp {m.Hp}/{m.MaxHp}{flag}");
        }
    }

    /// <summary>True if the straight line (x0,y0)-(x1,y1) crosses a wall that blocks (not flagged passable) anywhere in the room.</summary>
    bool SegmentCrossesWall(float x0, float y0, float x1, float y1, out int wallIdx)
    {
        wallIdx = -1;
        if (currentRoom == null) return false;
        var walls = currentRoom.Walls;
        for (int i = 0; i < walls.Count; i++)
        {
            var w = walls[i];
            bool blocks = false;
            foreach (int sdn in new[] { w.PosSideDef, w.NegSideDef })
                if (sdn > 0 && sdn <= currentRoom.SideDefs.Count && (currentRoom.SideDefs[sdn - 1].Flags & 0x4) == 0) blocks = true;
            if (!blocks) continue;
            float d1 = Cross(w.X0, w.Y0, w.X1, w.Y1, x0, y0), d2 = Cross(w.X0, w.Y0, w.X1, w.Y1, x1, y1);
            float d3 = Cross(x0, y0, x1, y1, w.X0, w.Y0), d4 = Cross(x0, y0, x1, y1, w.X1, w.Y1);
            if (d1 * d2 < 0 && d3 * d4 < 0) { wallIdx = i; return true; }
        }
        return false;
    }
    static float Cross(float ax, float ay, float bx, float by, float px, float py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    /// <summary>A server report for another object (topic "move"): where it says the object is, and whether that step crossed a wall.</summary>
    void DbgMoveReport(uint id, float x, float y, float spd)
    {
        if (!dbg.Enabled || !dbg.Movement) return;
        string name = client.Objects.Find(o => o.ID == id) is { } ob ? (client.Resolve(ob.NameRID) ?? "?") : "?";
        string cross = "";
        if (objReported.TryGetValue(id, out var prev) && SegmentCrossesWall(prev.x, prev.y, x, y, out int wi)) cross = $"  ** SERVER STEP CROSSES WALL #{wi} **";
        objPos.TryGetValue(id, out var shown);
        Dbg("move", $"REPORT #{id} {name} -> ({x / 1024f:F2},{y / 1024f:F2}) spd {spd:F0}; drawn at ({shown.x / 1024f:F2},{shown.y / 1024f:F2}) queue {(objQueue.TryGetValue(id, out var q) ? q.Count : 0)}{cross}");
    }

    readonly Dictionary<uint, (float x, float y)> dbgDrawn = new Dictionary<uint, (float, float)>();
    /// <summary>Every frame (from the glide loop): flag drawn steps that cross a wall.</summary>
    void DbgMoveDrawn(uint id, float ox, float oy, float nx, float ny)
    {
        if (!dbg.Enabled || !dbg.Movement) return;
        if (SegmentCrossesWall(ox, oy, nx, ny, out int wi)) Dbg("move", $"DRAWN #{id} crossed wall #{wi}: ({ox / 1024f:F2},{oy / 1024f:F2}) -> ({nx / 1024f:F2},{ny / 1024f:F2}) ** DRAWN THROUGH WALL **");
    }

    /// <summary>Chat marker: every line typed in the chat box is also written to the log (when it is on) together with a snapshot of the room,
    /// so a test can be bracketed ("start" ... "stop") and read back later.  Remove this method and the one call in Main.cs to drop the feature.</summary>
    void DbgMark(string text)
    {
        if (!dbg.Enabled || dbgWriter == null) return;
        string room = client.Room != null ? System.IO.Path.GetFileName(ResolveFile(client.Resolve(client.Room.RoomFileRID)) ?? "?") : "?";
        Dbg("mark", $"==== \"{text}\" ==== room {room}; player ({(walker != null ? walker.X / 1024f : 0):F1},{(walker != null ? walker.Y / 1024f : 0):F1})");
        foreach (var o in client.Objects)
        {
            if (o.ID == client.PlayerId || !objPos.TryGetValue(o.ID, out var p)) continue;
            var gm = client.Group.Find(m => m.Id == o.ID);
            string kind = gm != null ? (gm.Kind == 2 ? "companion" : "group") : o.Flags.IsAttackable ? "attackable" : o.Flags.IsPlayer ? "player" : "other";
            if (kind == "other") continue;
            Dbg("mark", $"  {kind} #{o.ID} {client.Resolve(o.NameRID) ?? "?"} at ({p.x / 1024f:F1},{p.y / 1024f:F1})");
        }
    }
}
