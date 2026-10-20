using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

/// <summary>Key bindings (Interface window, "Keys..." button), saved in userdata/keys.json. Hotbar slots stay on 1-0, the arrow keys always walk / turn as well.</summary>
public partial class Main
{
    static readonly (string id, string label, Key def)[] KeyActions =
    {
        ("forward", "Walk forward", Key.W), ("back", "Walk backward", Key.S), ("left", "Step left", Key.A), ("right", "Step right", Key.D),
        ("turnleft", "Turn left", Key.Left), ("turnright", "Turn right", Key.Right), ("run", "Run", Key.Shift),
        ("use", "Use / go through doors", Key.Space), ("attack", "Attack", Key.F), ("rest", "Rest / stand", Key.R), ("target", "Next target", Key.Tab),
        ("chat", "Chat", Key.Enter), ("camera", "Camera: first / third person", Key.V),
        ("inventory", "Inventory tab", Key.I), ("spells", "Spells tab", Key.B), ("skills", "Skills tab", Key.K),
    };
    readonly Dictionary<string, Key> keymap = KeyActions.ToDictionary(a => a.id, a => a.def);
    string KeysPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/keys.json"));

    Key KeyOf(string action) => keymap[action];
    bool Held(string action) => Input.IsKeyPressed(keymap[action]);
    bool Pressed(string action, InputEventKey k) => k.Keycode == keymap[action] || (action == "chat" && k.Keycode == Key.KpEnter);

    void LoadKeys()
    {
        try
        {
            if (!File.Exists(KeysPath)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(KeysPath));
            foreach (var kv in saved) if (keymap.ContainsKey(kv.Key) && Enum.TryParse<Key>(kv.Value, out var key)) keymap[kv.Key] = key;
        }
        catch { }
    }

    void SaveKeys()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(KeysPath)); File.WriteAllText(KeysPath, JsonSerializer.Serialize(keymap.ToDictionary(k => k.Key, k => k.Value.ToString()))); } catch { }
    }

    string capturing;                         // the action waiting for a key press
    readonly Dictionary<string, Button> keyButtons = new Dictionary<string, Button>();
    Label keyHint;

    static string KeyName(Key k) => k switch { Key.Left => "Left arrow", Key.Right => "Right arrow", Key.Up => "Up arrow", Key.Down => "Down arrow", Key.Enter => "Enter", Key.Space => "Space", _ => OS.GetKeycodeString(k) };

    void BuildKeysUi()
    {
        LoadKeys();
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 3);
        keyHint = new Label { Text = "Click a key, then press the new one (Esc cancels).", Modulate = new Color(1, 1, 1, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(keyHint);
        foreach (var (id, label, _) in KeyActions)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var b = new Button { Text = KeyName(keymap[id]), CustomMinimumSize = new Vector2(110, 0) };
            string a = id;
            b.Pressed += () => { capturing = a; keyHint.Text = "Press the new key for \"" + label + "\" ..."; RefreshKeyButtons(); };
            keyButtons[id] = b; row.AddChild(b); box.AddChild(row);
        }
        var reset = new Button { Text = "Reset to defaults" };
        reset.Pressed += () => { foreach (var a in KeyActions) keymap[a.id] = a.def; capturing = null; SaveKeys(); RefreshKeyButtons(); keyHint.Text = "Keys reset."; };
        box.AddChild(reset);
        var scroll = new ScrollContainer { ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(box);
        var w = hudMgr.Add("keys", "Keys", scroll, new Vector2(0.36f, 0.12f), new Vector2(330, 480), visibleByDefault: false);
        w.Visible = false;
        var open = new Button { Text = "Keys..." };
        open.Pressed += () => hudMgr.Toggle("keys");
        hudMgr.InterfaceBox?.AddChild(open);
    }

    void RefreshKeyButtons()
    {
        foreach (var kv in keyButtons) kv.Value.Text = capturing == kv.Key ? "..." : KeyName(keymap[kv.Key]);
    }

    /// <summary>While a key is being chosen, the next key press is taken as the binding. Returns true when it used the event.</summary>
    bool CaptureKey(InputEventKey k)
    {
        if (capturing == null) return false;
        var act = capturing; capturing = null;
        if (k.Keycode == Key.Escape) { keyHint.Text = "Cancelled."; RefreshKeyButtons(); return true; }
        // a key belongs to one action: the action that had it loses it and gets the old key of this one
        var other = keymap.Where(kv => kv.Key != act && kv.Value == k.Keycode).Select(kv => kv.Key).FirstOrDefault();
        if (other != null) keymap[other] = keymap[act];
        keymap[act] = k.Keycode;
        SaveKeys(); RefreshKeyButtons();
        keyHint.Text = other != null ? "Done. The key was in use, so the two keys swapped." : "Done.";
        return true;
    }
}
