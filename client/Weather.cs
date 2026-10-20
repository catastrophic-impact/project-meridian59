using System;
using Godot;
using Meridian59.Data.Models;

/// <summary>Weather the server announces: rain of five strengths (sprinkle .. downpour, BP_EXT_GAME weather message), sometimes a thunderstorm with lightning and thunder,
/// snow (kept for the cold north, later) and the sandstorm (official d3drender_fx.c: 8 emitters round the player firing orange lines, one particle per emitter every step,
/// 40 steps of life, steps run at 70 per second).  The original client does not draw rain or snow at all, so this look is our own.
/// Rain and snow fall in a box round the player and only where nothing roofs the spot, so it rains outdoors only.  Overcast darkens the sky and the ambient light outdoors.
/// Positions are client fine units (x east, y south, z up, 1024 per square).</summary>
public partial class Main
{
    bool wxSnow, wxSand;
    int wxIntensity; bool wxStorm;                 // rain: 0 clear .. 5 downpour, thunderstorm
    float wxOvercast, wxFlash, wxOutdoor;          // eased: darkness of the sky, lightning flash, "am I standing under open sky"
    string wxTestMode = System.Environment.GetEnvironmentVariable("M59_WEATHER");   // test hook: rain | snow | sand | all  (M59_WXINT = strength, M59_WXSTORM = 1)
    MultiMeshInstance3D wxRainMesh, wxSnowMesh, wxSplashMesh; ImmediateMesh wxSandLines; MeshInstance3D wxSandNode;
    AudioStreamPlayer wxRainSound, wxThunderSound; static AudioStreamWav rainLoop, thunderClip;
    const int RainCount = 1500, SnowCount = 900, SplashCount = 90;
    readonly float[] rx = new float[RainCount], ry = new float[RainCount], rz = new float[RainCount], rv = new float[RainCount];
    readonly float[] sx = new float[SnowCount], sy = new float[SnowCount], sz = new float[SnowCount], sv = new float[SnowCount], sph = new float[SnowCount];
    readonly float[] spx = new float[SplashCount], spy = new float[SplashCount], spz = new float[SplashCount], spt = new float[SplashCount];   // splash: where, how far through its short life (0..1, >1 = free)
    const float WxRadius = 6.5f * 1024f, WxTop = 3.4f * 1024f;      // the box of weather round the player
    double wxSteps, wxStrikeIn = 40, wxThunderIn = -1, wxFlashT = -1; float wxThunderVol; readonly Random wxRng = new Random();
    static readonly float[] RainDensity = { 0f, 0.14f, 0.32f, 0.58f, 0.82f, 1f };      // by strength: share of the drops shown
    static readonly float[] RainOvercast = { 0f, 0.18f, 0.34f, 0.52f, 0.70f, 0.84f };  // by strength: how dark the sky gets (a storm adds a little)

    // sandstorm particles (d3dparticle.c): position, velocity (per step), steps left
    const int SandEmitters = 8, SandLife = 40;
    struct SandP { public float x, y, z, vx, vy; public int left; }
    readonly SandP[] sand = new SandP[SandEmitters * SandLife];
    int sandNext;

    void BuildWeather()
    {
        MultiMeshInstance3D make(int n, bool fixedY, Color col, bool colors = false)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, InstanceCount = n, Mesh = new QuadMesh { Size = Vector2.One } };
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = col, VertexColorUseAsAlbedo = colors,
                BillboardMode = fixedY ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, DisableFog = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var node = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4096 };
            AddChild(node);
            return node;
        }
        wxRainMesh = make(RainCount, true, new Color(0.80f, 0.86f, 1f, 0.55f));
        wxSnowMesh = make(SnowCount, false, new Color(1f, 1f, 1f, 0.9f));
        wxSplashMesh = make(SplashCount, false, new Color(0.85f, 0.9f, 1f, 1f), colors: true);
        for (int i = 0; i < SplashCount; i++) spt[i] = 2f;
        wxSandLines = new ImmediateMesh();
        wxSandNode = new MeshInstance3D
        {
            Mesh = wxSandLines, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4096,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, DisableFog = true },
        };
        AddChild(wxSandNode);
        rainLoop ??= MakeRainLoop(); thunderClip ??= MakeThunder();
        wxRainSound = new AudioStreamPlayer { Stream = rainLoop, VolumeDb = -80f }; AddChild(wxRainSound);
        wxThunderSound = new AudioStreamPlayer { Stream = thunderClip }; AddChild(wxThunderSound);
        client.WeatherReceived += (i, storm) => { bool was = wxStorm; wxIntensity = i; wxStorm = storm && i >= 3; if (i == 0) wxThunderIn = -1; if (wxStorm && !was) wxStrikeIn = Rnd(6f, 25f); };
        if (System.Environment.GetEnvironmentVariable("M59_WXSTORM") == "1") wxStorm = true;
    }

    // ---- sounds made in code (no recordings needed; a recording can replace them later)
    static AudioStreamWav MakeRainLoop()
    {
        const int rate = 22050, n = rate * 3; var d = new byte[n * 2]; var rnd = new Random(7); float lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float w = (float)(rnd.NextDouble() * 2 - 1);
            lp += (w - lp) * 0.45f; lp2 += (lp - lp2) * 0.25f;
            float v = (lp * 0.5f + (lp - lp2) * 0.9f + w * 0.12f) * 0.55f;
            short s = (short)(Mathf.Clamp(v, -1f, 1f) * 32000f); d[i * 2] = (byte)(s & 0xFF); d[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = n, Data = d };
    }

    static AudioStreamWav MakeThunder()
    {
        const int rate = 22050, n = rate * 6; var d = new byte[n * 2]; var rnd = new Random(11); float b = 0, lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate, w = (float)(rnd.NextDouble() * 2 - 1);
            b = b * 0.994f + w * 0.12f; lp += (w - lp) * 0.08f;                       // low rumble plus a muffled crack
            float env = (1f - Mathf.Exp(-t / 0.06f)) * Mathf.Exp(-t / 1.5f);
            float roll = 0.65f + 0.35f * Mathf.Sin(t * 9f + Mathf.Sin(t * 3.1f) * 2f);   // rolling in waves
            float crack = Mathf.Exp(-t / 0.18f) * lp * 1.4f;
            float v = (b * 3.2f * roll + crack) * env * 0.9f;
            short s = (short)(Mathf.Clamp(v, -1f, 1f) * 30000f); d[i * 2] = (byte)(s & 0xFF); d[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = d };
    }

    void OnWeatherEffect(Effect ef)
    {
        switch (ef)
        {
            case EffectRaining: if (wxIntensity == 0) wxIntensity = 3; break;       // a server that sends no strength: moderate rain
            case EffectSnowing: wxSnow = true; break;
            case EffectClearWeather: wxIntensity = 0; wxStorm = false; wxSnow = false; wxThunderIn = -1; break;
            case EffectSand: wxSand = true; break;
            case EffectClearSand: wxSand = false; break;
        }
    }

    void ResetWeather()
    {
        wxIntensity = 0; wxStorm = false; wxSnow = wxSand = false; wxThunderIn = -1; wxFlashT = -1;
        for (int i = 0; i < sand.Length; i++) sand[i].left = 0;
    }

    float Rnd(float a, float b) => a + (float)wxRng.NextDouble() * (b - a);

    /// <summary>Is the spot open to the sky (a floor under it, nothing roofing it)?  Also gives the floor height.</summary>
    bool OpenSky(float x, float y, out float floor)
    {
        floor = 0;
        int sn = currentRoom.FindSector(x, y);
        if (sn == 0) return false;
        var s = currentRoom.Sectors[sn - 1];
        floor = s.FloorHeight * 16f;
        return s.CeilingBmp == 0;
    }

    void UpdateWeather(float dt)
    {
        if (wxRainMesh == null) return;
        dt = Mathf.Min(dt, 0.1f);
        bool ready = roomShown && walker != null && currentRoom != null && !flyMode;
        bool on = audioSettings.Weather && ready;
        int inten = wxIntensity;
        bool storm = wxStorm;
        if (wxTestMode == "rain" || wxTestMode == "all") { inten = int.TryParse(System.Environment.GetEnvironmentVariable("M59_WXINT"), out var ti) ? ti : 3; storm = wxStorm || System.Environment.GetEnvironmentVariable("M59_WXSTORM") == "1"; }
        bool rain = on && inten > 0;
        bool snow = on && (wxSnow || wxTestMode == "snow" || wxTestMode == "all");
        bool sandOn = on && (wxSand || wxTestMode == "sand" || wxTestMode == "all");
        wxRainMesh.Visible = rain; wxSnowMesh.Visible = snow; wxSandNode.Visible = sandOn; wxSplashMesh.Visible = rain;

        // outdoors or in: eases over a second so walking through a door fades the weather in and out
        float open = ready && OpenSky(walker.X, walker.Y, out _) ? 1f : 0f;
        wxOutdoor = Mathf.MoveToward(wxOutdoor, open, dt * 1.2f);

        // overcast and lightning
        float targetOver = rain ? Mathf.Min(1f, RainOvercast[Math.Clamp(inten, 0, 5)] + (storm ? 0.12f : 0f)) * wxOutdoor : 0f;
        wxOvercast = Mathf.MoveToward(wxOvercast, targetOver, dt * 0.25f);
        UpdateLightning(dt, rain && storm, inten, ready);
        RoomLight.SetDim(wxOvercast * 0.8f);
        SetSkyWeather(wxOvercast, wxFlash * wxOutdoor);
        UpdateWeatherSound(dt, rain, inten);

        if (rain) { float px = walker.X, py = walker.Y, eye = walker.Z + Walker.EyeHeight; UpdateRain(dt, px, py, eye, inten, storm); UpdateSplashes(dt, px, py, inten); }
        if (!rain && !snow && !sandOn) { if (wxSandLines.GetSurfaceCount() > 0) wxSandLines.ClearSurfaces(); return; }
        float qx = walker.X, qy = walker.Y, qe = walker.Z + Walker.EyeHeight;
        if (snow) UpdateSnow(dt, qx, qy, qe);
        if (sandOn) UpdateSand(dt, qx, qy, qe); else if (wxSandLines.GetSurfaceCount() > 0) wxSandLines.ClearSurfaces();
    }

    // ---- lightning (thunderstorms only; a strike every half minute to two minutes, a little more often in a downpour)
    void UpdateLightning(float dt, bool stormy, int inten, bool ready)
    {
        if (wxFlashT >= 0)
        {   // a flash: two quick pulses and a fade
            wxFlashT += dt;
            float t = (float)wxFlashT;
            wxFlash = t < 0.06f ? 1f : t < 0.12f ? 0.25f : t < 0.20f ? 0.85f : Mathf.Max(0f, 0.6f * (1f - (t - 0.20f) / 0.5f));
            if (t > 0.75f) { wxFlashT = -1; wxFlash = 0; }
        }
        else wxFlash = 0;
        if (wxThunderIn >= 0)
        {
            wxThunderIn -= dt;
            if (wxThunderIn < 0 && audioSettings.Weather)
            {
                wxThunderSound.PitchScale = Rnd(0.85f, 1.1f);
                wxThunderSound.VolumeDb = Mathf.LinearToDb(Mathf.Clamp((float)audioSettings.Sfx * wxThunderVol * (wxOutdoor > 0.5f ? 1f : 0.5f), 0.001f, 1f));
                wxThunderSound.Play(); wxThunderIn = -1;
            }
        }
        if (!stormy || !ready) return;
        wxStrikeIn -= dt;
        if (wxStrikeIn > 0) return;
        wxStrikeIn = Rnd(30f, 120f) / (1f + (inten - 3) * 0.25f);
        wxFlashT = 0;
        float distance = Rnd(0.3f, 7f);                       // seconds the thunder takes to arrive
        wxThunderIn = distance; wxThunderVol = Mathf.Clamp(1f - distance / 9f, 0.25f, 1f);
        if (AnimLog) GD.Print($"[weather] lightning, thunder in {distance:F1}s, next strike in {wxStrikeIn:F0}s");
    }

    void UpdateWeatherSound(float dt, bool rain, int inten)
    {
        float level = rain ? 0.12f + 0.16f * inten : 0f;                           // louder the harder it rains
        float vol = rain ? (float)audioSettings.Sfx * level * (0.35f + 0.65f * wxOutdoor) : 0f;    // muffled indoors
        float db = vol <= 0.002f ? -80f : Mathf.LinearToDb(vol);
        wxRainSound.VolumeDb = Mathf.Lerp(wxRainSound.VolumeDb, db, Mathf.Min(1f, dt * 2f));
        if (db > -70f && !wxRainSound.Playing) wxRainSound.Play();
        else if (db <= -70f && wxRainSound.VolumeDb < -70f && wxRainSound.Playing) wxRainSound.Stop();
    }

    void WrapAround(ref float x, ref float y, float px, float py)
    {
        // keep the box centred on the player: a drop that left it comes back in on the other side
        if (x < px - WxRadius) x += 2 * WxRadius; else if (x > px + WxRadius) x -= 2 * WxRadius;
        if (y < py - WxRadius) y += 2 * WxRadius; else if (y > py + WxRadius) y -= 2 * WxRadius;
    }

    void UpdateRain(float dt, float px, float py, float eye, int inten, bool storm)
    {
        var mm = wxRainMesh.Multimesh;
        int shown = (int)(RainCount * RainDensity[Math.Clamp(inten, 0, 5)]);
        float slant = 1000f + inten * 120f + (storm ? 900f : 0f), speedK = 0.8f + 0.07f * inten;
        ((StandardMaterial3D)wxRainMesh.MaterialOverride).AlbedoColor = new Color(0.80f, 0.86f, 1f, 0.34f + 0.05f * inten);
        for (int i = 0; i < RainCount; i++)
        {
            if (i >= shown) { mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero)); rv[i] = 0; continue; }
            if (rv[i] == 0) { rx[i] = px + Rnd(-WxRadius, WxRadius); ry[i] = py + Rnd(-WxRadius, WxRadius); rz[i] = eye + Rnd(-3 * 1024f, WxTop); rv[i] = Rnd(8500f, 10500f); }
            rz[i] -= rv[i] * speedK * dt; rx[i] += slant * dt;
            WrapAround(ref rx[i], ref ry[i], px, py);
            bool open = OpenSky(rx[i], ry[i], out var floor);
            if (rz[i] < floor || rz[i] < eye - 9 * 1024f)
            {
                if (open && rz[i] < floor + 200f && inten > 0) Splash(rx[i], ry[i], floor);
                rx[i] = px + Rnd(-WxRadius, WxRadius); ry[i] = py + Rnd(-WxRadius, WxRadius); rz[i] = eye + Rnd(0, WxTop);
            }
            if (!open || rz[i] < floor) { mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero)); continue; }
            // a thin streak, billboarded round the vertical axis
            mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(0.03f, 0.7f, 1f)), RoomBuilder.P(rx[i], ry[i], rz[i])));
        }
    }

    // ---- tiny splashes where drops hit the ground close by
    void Splash(float x, float y, float floor)
    {
        float dx = x - walker.X, dy = y - walker.Y;
        if (dx * dx + dy * dy > 3.2f * 1024f * 3.2f * 1024f) return;     // only near the player
        for (int i = 0; i < SplashCount; i++) if (spt[i] >= 1f) { spx[i] = x; spy[i] = y; spz[i] = floor + 20f; spt[i] = 0f; return; }
    }

    void UpdateSplashes(float dt, float px, float py, int inten)
    {
        var mm = wxSplashMesh.Multimesh;
        for (int i = 0; i < SplashCount; i++)
        {
            if (spt[i] >= 1f) { mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero)); continue; }
            spt[i] += dt / 0.22f;
            float k = Mathf.Clamp(spt[i], 0f, 1f), size = 0.05f + 0.13f * k;
            mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(size, size * 0.5f, 1f)), RoomBuilder.P(spx[i], spy[i], spz[i])));
            mm.SetInstanceColor(i, new Color(0.85f, 0.9f, 1f, 0.5f * (1f - k)));
        }
    }

    void UpdateSnow(float dt, float px, float py, float eye)
    {
        var mm = wxSnowMesh.Multimesh; double t = Time.GetTicksMsec() / 1000.0;
        for (int i = 0; i < SnowCount; i++)
        {
            if (sv[i] == 0) { sx[i] = px + Rnd(-WxRadius, WxRadius); sy[i] = py + Rnd(-WxRadius, WxRadius); sz[i] = eye + Rnd(-3 * 1024f, WxTop); sv[i] = Rnd(500f, 950f); sph[i] = Rnd(0, 6.28f); }
            sz[i] -= sv[i] * dt;
            sx[i] += (float)Math.Sin(t * 0.9 + sph[i]) * 260f * dt + 150f * dt; sy[i] += (float)Math.Cos(t * 0.7 + sph[i]) * 200f * dt;
            WrapAround(ref sx[i], ref sy[i], px, py);
            bool open = OpenSky(sx[i], sy[i], out var floor);
            if (sz[i] < floor || sz[i] < eye - 9 * 1024f) { sx[i] = px + Rnd(-WxRadius, WxRadius); sy[i] = py + Rnd(-WxRadius, WxRadius); sz[i] = eye + Rnd(0, WxTop); }
            if (!open || sz[i] < floor) { mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero)); continue; }
            float size = 0.10f + 0.06f * (sph[i] / 6.28f);
            mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(size, size, 1f)), RoomBuilder.P(sx[i], sy[i], sz[i])));
        }
    }

    void UpdateSand(float dt, float px, float py, float eye)
    {
        wxSteps += dt * 70.0;
        int steps = (int)wxSteps; wxSteps -= steps;
        const float Radius = 10000f, ZVar = 1000f, Vel = 400f;
        for (int s = 0; s < steps; s++)
        {
            for (int i = 0; i < sand.Length; i++) if (sand[i].left > 0) { sand[i].x += sand[i].vx; sand[i].y += sand[i].vy; sand[i].left--; }
            for (int e = 0; e < SandEmitters; e++)   // every emitter fires one particle each step, in its own direction (a full circle in 45 degree steps)
            {
                float a = e * 2f * Mathf.Pi / SandEmitters;
                sand[sandNext] = new SandP { x = px + Rnd(-Radius, Radius), y = py + Rnd(-Radius, Radius), z = eye + Rnd(-ZVar, 2 * ZVar), vx = Mathf.Cos(a) * Vel, vy = Mathf.Sin(a) * Vel, left = SandLife };
                sandNext = (sandNext + 1) % sand.Length;
            }
        }
        wxSandLines.ClearSurfaces();
        wxSandLines.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var head = new Color(226 / 255f, 153 / 255f, 6 / 255f, 1f); var tail = new Color(head.R, head.G, head.B, 0f);
        for (int i = 0; i < sand.Length; i++)
        {
            if (sand[i].left <= 0) continue;
            var p = sand[i];
            wxSandLines.SurfaceSetColor(head); wxSandLines.SurfaceAddVertex(RoomBuilder.P(p.x, p.y, p.z));
            wxSandLines.SurfaceSetColor(tail); wxSandLines.SurfaceAddVertex(RoomBuilder.P(p.x - p.vx, p.y - p.vy, p.z));
        }
        wxSandLines.SurfaceEnd();
    }
}
