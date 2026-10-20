using System;
using Godot;
using Meridian59.Data.Models;

/// <summary>Weather the server announces with BP_EFFECT: rain, snow (our own look: the original client's rain and snow code is disabled in its source) and the sandstorm
/// (official d3drender_fx.c: 8 emitters round the player firing orange lines, one particle per emitter every step, 40 steps of life, steps run at 70 per second).
/// Rain and snow fall in a box round the player and only where nothing roofs the spot or the floor is below, so it rains outdoors only.
/// Positions are client fine units (x east, y south, z up, 1024 per square).</summary>
public partial class Main
{
    bool wxRain, wxSnow, wxSand;
    string wxTestMode = System.Environment.GetEnvironmentVariable("M59_WEATHER");   // test hook: rain | snow | sand | all
    MultiMeshInstance3D wxRainMesh, wxSnowMesh; ImmediateMesh wxSandLines; MeshInstance3D wxSandNode;
    const int RainCount = 1500, SnowCount = 900;
    readonly float[] rx = new float[RainCount], ry = new float[RainCount], rz = new float[RainCount], rv = new float[RainCount];
    readonly float[] sx = new float[SnowCount], sy = new float[SnowCount], sz = new float[SnowCount], sv = new float[SnowCount], sph = new float[SnowCount];
    const float WxRadius = 6.5f * 1024f, WxTop = 3.4f * 1024f;      // the box of weather round the player
    double wxSteps; readonly Random wxRng = new Random();

    // sandstorm particles (d3dparticle.c): position, velocity (per step), steps left
    const int SandEmitters = 8, SandLife = 40;
    struct SandP { public float x, y, z, vx, vy; public int left; }
    readonly SandP[] sand = new SandP[SandEmitters * SandLife];
    int sandNext;

    void BuildWeather()
    {
        MultiMeshInstance3D make(int n, bool fixedY, Color col)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = n, Mesh = new QuadMesh { Size = Vector2.One } };
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = col,
                BillboardMode = fixedY ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, DisableFog = true,
                NoDepthTest = false, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var node = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4096 };
            AddChild(node);
            return node;
        }
        wxRainMesh = make(RainCount, true, new Color(0.80f, 0.86f, 1f, 0.55f));
        wxSnowMesh = make(SnowCount, false, new Color(1f, 1f, 1f, 0.9f));
        wxSandLines = new ImmediateMesh();
        wxSandNode = new MeshInstance3D
        {
            Mesh = wxSandLines, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 4096,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, DisableFog = true },
        };
        AddChild(wxSandNode);
        for (int i = 0; i < RainCount; i++) rv[i] = 0;
    }

    void OnWeatherEffect(Effect ef)
    {
        switch (ef)
        {
            case EffectRaining: wxRain = true; break;
            case EffectSnowing: wxSnow = true; break;
            case EffectClearWeather: wxRain = wxSnow = false; break;
            case EffectSand: wxSand = true; break;
            case EffectClearSand: wxSand = false; break;
        }
    }

    void ResetWeather() { wxRain = wxSnow = wxSand = false; for (int i = 0; i < sand.Length; i++) sand[i].left = 0; }

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
        bool on = audioSettings.Weather && roomShown && walker != null && currentRoom != null && !flyMode;
        bool rain = on && (wxRain || wxTestMode == "rain" || wxTestMode == "all");
        bool snow = on && (wxSnow || wxTestMode == "snow" || wxTestMode == "all");
        bool sandOn = on && (wxSand || wxTestMode == "sand" || wxTestMode == "all");
        wxRainMesh.Visible = rain; wxSnowMesh.Visible = snow; wxSandNode.Visible = sandOn;
        if (!rain && !snow && !sandOn) return;
        float px = walker.X, py = walker.Y, eye = walker.Z + Walker.EyeHeight;
        dt = Mathf.Min(dt, 0.1f);

        if (rain) UpdateRain(dt, px, py, eye);
        if (snow) UpdateSnow(dt, px, py, eye);
        if (sandOn) UpdateSand(dt, px, py, eye); else if (wxSandLines.GetSurfaceCount() > 0) wxSandLines.ClearSurfaces();
    }

    void WrapAround(ref float x, ref float y, float px, float py)
    {
        // keep the box centred on the player: a drop that left it comes back in on the other side
        if (x < px - WxRadius) x += 2 * WxRadius; else if (x > px + WxRadius) x -= 2 * WxRadius;
        if (y < py - WxRadius) y += 2 * WxRadius; else if (y > py + WxRadius) y -= 2 * WxRadius;
    }

    void UpdateRain(float dt, float px, float py, float eye)
    {
        var mm = wxRainMesh.Multimesh;
        for (int i = 0; i < RainCount; i++)
        {
            if (rv[i] == 0) { rx[i] = px + Rnd(-WxRadius, WxRadius); ry[i] = py + Rnd(-WxRadius, WxRadius); rz[i] = eye + Rnd(-3 * 1024f, WxTop); rv[i] = Rnd(8500f, 10500f); }
            rz[i] -= rv[i] * dt; rx[i] += 1100f * dt;
            WrapAround(ref rx[i], ref ry[i], px, py);
            bool open = OpenSky(rx[i], ry[i], out var floor);
            if (rz[i] < floor || rz[i] < eye - 9 * 1024f) { rx[i] = px + Rnd(-WxRadius, WxRadius); ry[i] = py + Rnd(-WxRadius, WxRadius); rz[i] = eye + Rnd(0, WxTop); }
            if (!open || rz[i] < floor) { mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero)); continue; }
            // a thin streak, longer the faster it falls; billboarded round the vertical axis
            mm.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(0.03f, 0.7f, 1f)), RoomBuilder.P(rx[i], ry[i], rz[i])));
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
