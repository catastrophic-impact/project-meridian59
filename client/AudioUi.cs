using System;
using System.IO;
using System.Text.Json;
using Godot;

/// <summary>Music / sound volume sliders in the Interface window (F10), saved in userdata/audio.json.</summary>
public partial class Main
{
    class AudioSettings { public double Music { get; set; } = 0.6; public double Sfx { get; set; } = 0.9; public bool ScrollWater { get; set; } = true; public bool CycleWalls { get; set; } = true; public bool Weather { get; set; } = true; public bool ShowDebug { get; set; } = false; }
    AudioSettings audioSettings = new AudioSettings();
    string AudioPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/audio.json"));

    void LoadAudioSettings()
    {
        try { if (File.Exists(AudioPath)) audioSettings = JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(AudioPath)) ?? audioSettings; } catch { }
        audio.MusicVolume = (float)audioSettings.Music; audio.SfxVolume = (float)audioSettings.Sfx;
    }

    void SaveAudioSettings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(AudioPath)); File.WriteAllText(AudioPath, JsonSerializer.Serialize(audioSettings)); } catch { }
    }

    void BuildAudioSliders()
    {
        var box = hudMgr?.InterfaceBox; if (box == null) return;
        Control row(string text, double value, Action<double> set)
        {
            var v = new VBoxContainer();
            var l = new Label { Text = $"{text}: {(int)Math.Round(value * 100)}%" };
            var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(180, 20) };
            s.ValueChanged += x => { l.Text = $"{text}: {(int)Math.Round(x * 100)}%"; set(x); };
            s.DragEnded += _ => SaveAudioSettings();
            v.AddChild(l); v.AddChild(s); return v;
        }
        var scroll = new CheckButton { Text = "Scrolling water / walls", ButtonPressed = audioSettings.ScrollWater, TooltipText = "The newer official client scrolls some water and wall textures (flagged in the room files); the old software client did not. Turn off for still water." };
        scroll.Toggled += on => { audioSettings.ScrollWater = on; SaveAudioSettings(); };
        var cycle = new CheckButton { Text = "Flashing wall textures (gates)", ButtonPressed = audioSettings.CycleWalls, TooltipText = "Some walls, like iron gates, switch between texture frames several times a second, as in the original client. Turn off to show a still picture." };
        cycle.Toggled += on => { audioSettings.CycleWalls = on; SaveAudioSettings(); };
        box.AddChild(scroll);
        box.AddChild(cycle);
        var wx = new CheckButton { Text = "Weather (rain, snow, sand)", ButtonPressed = audioSettings.Weather, TooltipText = "Draw the rain, snow and sandstorms the server sends. Turn off for a clear view." };
        wx.Toggled += on => { audioSettings.Weather = on; SaveAudioSettings(); };
        box.AddChild(wx);
        box.AddChild(row("Music", audioSettings.Music, x => { audioSettings.Music = x; audio.SetMusicVolume((float)x); }));
        box.AddChild(row("Sound effects", audioSettings.Sfx, x => { audioSettings.Sfx = x; audio.SetSfxVolume((float)x); }));
    }
}
