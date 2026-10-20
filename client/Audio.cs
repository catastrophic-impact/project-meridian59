using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>Sound effects + music, following the official client rules (clientd3d/game.c GamePlaySound, sound.c, music.c):
/// volume scale 0..50; a sound from an object fades as 100/distance beyond 2 squares; a looping sound at a map position falls off linearly to its radius
/// (default 16 squares); looping sounds stop on room change; the same music file is not restarted; legacy .wav/.mid/.mp3 names map to .ogg.</summary>
public partial class GameAudio : Node
{
    const float OneShotBoostDb = 9f;
    const float MaxVolume = 50f, DefaultCutoff = 16f;
    public float MusicVolume = 0.6f, SfxVolume = 0.9f;
    public Func<string, string> ResolveFile;              // file name -> full path (case-insensitive) or null
    public Func<uint, Vector3?> ObjectPos;                // object id -> Godot position
    public Func<Vector3?> ListenerPos;                    // player position (Godot units); 1 grid square = 4 units
    const float Square = 4f;

    readonly Dictionary<string, AudioStream> cache = new Dictionary<string, AudioStream>();
    readonly HashSet<string> missing = new HashSet<string>();
    AudioStreamPlayer music; string musicFile;
    readonly List<Loop> loops = new List<Loop>();
    int oneShots;
    static readonly bool Verbose = System.Environment.GetEnvironmentVariable("M59_AUDIOLOG") == "1";

    class Loop { public AudioStreamPlayer3D Player; public AudioStreamPlayer Flat; public string File; public Vector3 Pos; public uint ObjId; public float Radius, MaxVol; public bool Positional; }

    public override void _Ready()
    {
        music = new AudioStreamPlayer { VolumeDb = -80 };
        AddChild(music);
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -1f, PreGainDb = 0f });   // keeps boosted one-shots from clipping
    }

    string Locate(string name)
    {
        if (string.IsNullOrEmpty(name) || ResolveFile == null) return null;
        string ext = Path.GetExtension(name).ToLowerInvariant();
        string n = (ext == ".wav" || ext == ".mid" || ext == ".midi" || ext == ".mp3") ? Path.ChangeExtension(name, ".ogg") : name;
        var p = ResolveFile(n);
        if (p == null && missing.Add(n.ToLowerInvariant())) GD.Print($"[audio] missing file for '{name}'");
        return p;
    }

    AudioStreamOggVorbis Load(string path, bool loop)
    {
        try
        {
            var st = AudioStreamOggVorbis.LoadFromFile(path);
            if (st != null) st.Loop = loop;
            return st;
        }
        catch (Exception ex) { GD.PrintErr("[audio] " + ex.Message); return null; }
    }

    AudioStream Cached(string path)
    {
        if (!cache.TryGetValue(path, out var st)) cache[path] = st = Load(path, false);
        return st;
    }

    static float Db(float lin) => lin <= 0.001f ? -80f : Mathf.LinearToDb(lin);

    // ---------------------------------------------------------------- music
    public void PlayMusic(string name)
    {
        if (string.IsNullOrEmpty(name)) { StopMusic(); return; }
        var path = Locate(name);
        if (path == null) return;
        if (musicFile != null && string.Equals(musicFile, path, StringComparison.OrdinalIgnoreCase) && music.Playing) return;   // same file: keep playing
        var st = Load(path, true);
        if (st == null) return;
        musicFile = path;
        GD.Print($"[audio] music {Path.GetFileName(path)}");
        var old = music;
        var fresh = new AudioStreamPlayer { Stream = st, VolumeDb = -80 };
        AddChild(fresh); fresh.Play();
        music = fresh;
        var tw = CreateTween().SetParallel(true);
        tw.TweenProperty(fresh, "volume_db", Db(MusicVolume), 1.5);
        if (old != null) { tw.TweenProperty(old, "volume_db", -80.0, 1.0); tw.Chain().TweenCallback(Callable.From(() => old.QueueFree())); }
    }

    public void StopMusic()
    {
        musicFile = null;
        if (music != null && music.Playing)
        {
            var old = music;
            music = new AudioStreamPlayer { VolumeDb = -80 }; AddChild(music);
            var tw = CreateTween(); tw.TweenProperty(old, "volume_db", -80.0, 1.0); tw.TweenCallback(Callable.From(() => old.QueueFree()));
        }
    }

    public bool AnyPlaying => (music != null && music.Playing && music.VolumeDb > -70f) || loops.Count > 0;
    public void SetMusicVolume(float v) { MusicVolume = v; if (music != null && music.Playing) music.VolumeDb = Db(v); }
    public void SetSfxVolume(float v) { SfxVolume = v; }

    // ---------------------------------------------------------------- sound effects
    /// <summary>PlayWave: row/col are grid squares, radius/maxVol as sent by the server (0 = default).</summary>
    public void PlaySound(string name, uint srcObj, byte flags, int row, int col, int radius, int maxVol)
    {
        var path = Locate(name);
        if (path == null) return;
        bool loop = (flags & 1) != 0, randomPitch = (flags & 2) != 0;
        float cutoff = radius > 0 ? radius : DefaultCutoff;
        float maxv = Math.Min(maxVol > 0 ? maxVol : MaxVolume, MaxVolume);
        var listener = ListenerPos?.Invoke();

        float vol = MaxVolume; Vector3 pos = Vector3.Zero; bool positional = false;
        if (srcObj != 0)
        {
            var op = ObjectPos?.Invoke(srcObj);
            if (op.HasValue && listener.HasValue)
            {
                float d = Flat(listener.Value, op.Value) / Square;
                if (d > 2) vol = MaxVolume * 2f / d;
                pos = op.Value; positional = d > 0.75f;   // your own sounds (weapon hits, "ouch") come from where the camera is: no panning direction, play them flat
            }
        }
        else if (row > 0 || col > 0)
        {
            pos = new Vector3(col * Square, listener?.Y ?? 3f, row * Square); positional = true;
            vol = 0;
            if (listener.HasValue) { float d = Flat(listener.Value, pos) / Square; if (d < cutoff) vol = maxv - d * maxv / cutoff; }
            else vol = maxv;
        }

        if (loop)
        {
            foreach (var l in loops) if (string.Equals(l.File, path, StringComparison.OrdinalIgnoreCase)) return;   // already looping
            var st = Load(path, true); if (st == null) return;
            var pl = MakePlayer(st, positional, pos, vol, randomPitch, true);
            loops.Add(new Loop { Player = pl.p3, Flat = pl.flat, File = path, Pos = pos, ObjId = srcObj, Radius = cutoff, MaxVol = maxv, Positional = positional });
            GD.Print($"[audio] loop {Path.GetFileName(path)} vol {vol:F0} radius {cutoff}");
            return;
        }
        if (Verbose) GD.Print($"[audio] sfx {Path.GetFileName(path)} vol {vol:F0} active {oneShots} obj {srcObj} pos {(positional ? pos.ToString() : "-")}");
        if (vol <= 0.5f || oneShots > 24) return;
        var s = Cached(path); if (s == null) return;
        var (p3, flat) = MakePlayer(s, positional, pos, vol, randomPitch);
        oneShots++;
        if (p3 != null) p3.Finished += () => { oneShots--; p3.QueueFree(); };
        else flat.Finished += () => { oneShots--; flat.QueueFree(); };
    }

    (AudioStreamPlayer3D p3, AudioStreamPlayer flat) MakePlayer(AudioStream st, bool positional, Vector3 pos, float vol, bool randomPitch, bool loop = false)
    {
        float gain = Mathf.Clamp(vol / MaxVolume, 0f, 1f) * SfxVolume;
        float boostDb = loop ? 0f : OneShotBoostDb;   // short transients (hits, misses, ouch) are mastered about 15 dB under the music: lift them, the master limiter catches peaks
        float pitch = randomPitch ? 0.9f + GD.Randf() * 0.2f : 1f;
        if (positional)
        {
            var p = new AudioStreamPlayer3D { Stream = st, VolumeDb = Db(gain) + boostDb, PitchScale = pitch, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0f };
            AddChild(p); p.GlobalPosition = pos; p.Play();
            return (p, null);
        }
        var f = new AudioStreamPlayer { Stream = st, VolumeDb = Db(gain) + boostDb, PitchScale = pitch };
        AddChild(f); f.Play();
        return (null, f);
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Entering a new room: looping ambient sounds of the old room stop (music keeps going until the server changes it).</summary>
    public void RoomChanged()
    {
        foreach (var l in loops) { l.Player?.QueueFree(); l.Flat?.QueueFree(); }
        loops.Clear();
    }

    public override void _Process(double delta)
    {
        if (loops.Count == 0) return;
        var listener = ListenerPos?.Invoke();
        foreach (var l in loops)
        {
            if (!l.Positional || l.Player == null || !listener.HasValue) continue;
            if (l.ObjId != 0) { var op = ObjectPos?.Invoke(l.ObjId); if (op.HasValue) { l.Pos = op.Value; l.Player.GlobalPosition = l.Pos; } }
            float d = Flat(listener.Value, l.Pos) / Square;
            float vol = l.ObjId != 0 ? (d > 2 ? MaxVolume * 2f / d : MaxVolume) : (d < l.Radius ? l.MaxVol - d * l.MaxVol / l.Radius : 0f);
            l.Player.VolumeDb = Db(Mathf.Clamp(vol / MaxVolume, 0f, 1f) * SfxVolume);
        }
    }
}
