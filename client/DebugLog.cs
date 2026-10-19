using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

/// <summary>Optional debug log (Interface window, F10): one file, userdata/client_debug.log, overwritten every time the client starts. Switched on per topic
/// ("Companions", "Network") so it can be reused for different tests; off = no file is touched. Env M59_DEBUGLOG=companions,net also switches topics on.</summary>
public partial class Main
{
    class DebugSettings { public bool Enabled { get; set; } public bool Companions { get; set; } = true; public bool Network { get; set; } }
    DebugSettings dbg = new DebugSettings();
    string DebugSettingsPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/debuglog.json"));
    string DebugLogPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/client_debug.log"));
    StreamWriter dbgWriter; double dbgNext;
    readonly Dictionary<uint, (float x, float y, double still)> dbgLast = new Dictionary<uint, (float, float, double)>();

    void LoadDebugSettings()
    {
        try { if (File.Exists(DebugSettingsPath)) dbg = JsonSerializer.Deserialize<DebugSettings>(File.ReadAllText(DebugSettingsPath)) ?? dbg; } catch { }
        var env = System.Environment.GetEnvironmentVariable("M59_DEBUGLOG");
        if (!string.IsNullOrEmpty(env)) { dbg.Enabled = true; dbg.Companions = env.Contains("companion"); dbg.Network = env.Contains("net"); }
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
        try { dbgWriter.WriteLine($"{Time.GetTicksMsec() / 1000.0,8:F2} [{topic}] {msg}"); } catch { }
    }

    void BuildDebugLogUi()
    {
        var box = hudMgr?.InterfaceBox; if (box == null) return;
        var on = new CheckButton { Text = "Debug log (userdata/client_debug.log)", ButtonPressed = dbg.Enabled, TooltipText = "Writes what the client sees to userdata/client_debug.log, overwritten every time the client starts. Pick the topics below." };
        var comp = new CheckButton { Text = "  topic: companions (positions, distance, stuck)", ButtonPressed = dbg.Companions };
        var net = new CheckButton { Text = "  topic: network / messages", ButtonPressed = dbg.Network };
        on.Toggled += v => { dbg.Enabled = v; if (v) OpenDebugLog(); SaveDebugSettings(); };
        comp.Toggled += v => { dbg.Companions = v; SaveDebugSettings(); };
        net.Toggled += v => { dbg.Network = v; SaveDebugSettings(); };
        box.AddChild(on); box.AddChild(comp); box.AddChild(net);
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
}
