using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;

/// <summary>Spell/combat visuals: projectiles (BP_SHOOT) flying between two objects, and screen effects (BP_EFFECT: pain, whiteout, colour flash, invert,
/// shake, blind, paralyze). Rules ported from the official client (clientd3d/projectile.c, effect.c, d3drender_fx.c).</summary>
public partial class Main
{
    // ------------------------------------------------------------ projectiles
    class Proj
    {
        public Sprite3D S; public Vector3 From, To; public float Progress, Increment;
        public Meridian59.Data.Models.Animation Anim; public string Path; public int Angle;
        public int Key = int.MinValue, Bucket = -1;
    }
    readonly List<Proj> projectiles = new List<Proj>();
    const float ProjectileHeight = 480f;           // fineness above the floor (chest height)

    bool ObjectWorldPos(uint id, out float x, out float y)
    {
        if (id == client.PlayerId && walker != null) { x = walker.X; y = walker.Y; return true; }
        if (objPos.TryGetValue(id, out var p)) { x = p.x; y = p.y; return true; }
        x = y = 0; return false;
    }

    float FloorAt(float x, float y)
    {
        if (currentRoom == null) return 0f;
        int sn = currentRoom.FindSector(x, y);
        return sn > 0 ? currentRoom.Sectors[sn - 1].FloorHeight * 16f : 0f;
    }

    void OnProjectile(Projectile p)
    {
        if (!roomShown || objectsRoot == null) return;
        if (!ObjectWorldPos(p.Source.ID, out float sx, out float sy) || !ObjectWorldPos(p.Target.ID, out float tx, out float ty)) return;
        if (p.Source.ID == p.Target.ID) return;
        string path = ResolveFile(client.Resolve(p.OverlayFileRID));
        if (path == null) return;
        float dx = tx - sx, dy = ty - sy;
        float dist = Mathf.Sqrt(dx * dx + dy * dy) / 1024f;                    // grid squares
        float inc = (p.Speed == 0 || dist <= 0f) ? 1f : Mathf.Max(p.Speed / 1000f / dist, 1f / 2000f);   // progress per ms; never slower than 2 s total
        var proj = new Proj
        {
            From = RoomBuilder.P(sx, sy, FloorAt(sx, sy) + ProjectileHeight),
            To = RoomBuilder.P(tx, ty, FloorAt(tx, ty) + ProjectileHeight),
            Increment = inc, Anim = p.Animation, Path = path,
            Angle = ((int)Math.Round(Math.Atan2(dy, dx) / Math.PI / 2.0 * 4096.0) % 4096 + 4096) % 4096,
            S = new Sprite3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Shaded = false, DoubleSided = true,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Name = "projectile",
            },
        };
        proj.S.Position = proj.From;
        objectsRoot.AddChild(proj.S);
        projectiles.Add(proj);
        if (shotPath != null && System.Environment.GetEnvironmentVariable("M59_SHOTONPROJ") == "1") shotCountdown = 9;   // test: screenshot while it flies
        if (AnimLog) GD.Print($"[proj] {client.Resolve(p.OverlayFileRID)} {dist:F1} squares, speed {p.Speed}");
        UpdateProjectileFrame(proj, Time.GetTicksMsec());
    }

    void UpdateProjectileFrame(Proj pr, double nowMs)
    {
        var anim = pr.Anim;
        if (anim != null && !(anim is AnimationNone)) { anim.GroupMax = sprites.Groups(pr.Path); anim.Tick(nowMs, 0); }
        int viewer = (int)Math.Round((-yaw - Mathf.Pi / 2f) / Mathf.Tau * 4096f);
        int bucket = AvatarComposer.RelativeAngle(pr.Angle, viewer) / 256;
        int key = AvatarComposer.GroupOf(anim);
        if (bucket == pr.Bucket && key == pr.Key) return;
        pr.Bucket = bucket; pr.Key = key;
        var e = sprites.Get(pr.Path, key, (ushort)(bucket * 256 + 128));
        if (e == null) return;
        pr.S.Texture = e.Texture;
        pr.S.PixelSize = 16f * RoomBuilder.S / e.Shrink;
    }

    void UpdateProjectiles(float dt)
    {
        if (projectiles.Count == 0) return;
        double now = Time.GetTicksMsec();
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            var pr = projectiles[i];
            pr.Progress += pr.Increment * dt * 1000f;
            if (pr.Progress >= 1f) { pr.S.QueueFree(); projectiles.RemoveAt(i); continue; }
            pr.S.Position = pr.From.Lerp(pr.To, pr.Progress);
            UpdateProjectileFrame(pr, now);
        }
    }

    void ClearProjectiles() { foreach (var p in projectiles) if (IsInstanceValid(p.S)) p.S.QueueFree(); projectiles.Clear(); }

    // ------------------------------------------------------------ screen effects
    ColorRect fxFlash, fxBlind; ColorRect fxInvert;
    float fxPain, fxWhiteout, fxFlashMs, fxInvertMs, fxShake;       // remaining ms
    Color fxFlashColor; bool fxBlindOn; bool fxParalyzed;

    void BuildEffectsLayer()
    {
        BuildWeather();
        var layer = new CanvasLayer { Layer = 2 };            // above the first-person weapon (1), below the HUD (5)
        ColorRect full(Color c) { var r = new ColorRect { Color = c, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; r.SetAnchorsPreset(Control.LayoutPreset.FullRect); return r; }
        fxInvert = full(Colors.White);
        fxInvert.Material = new ShaderMaterial
        {
            Shader = new Shader { Code = "shader_type canvas_item;\nuniform sampler2D screen : hint_screen_texture, filter_nearest;\nvoid fragment() { COLOR = vec4(vec3(1.0) - texture(screen, SCREEN_UV).rgb, 1.0); }" }
        };
        fxFlash = full(new Color(1, 0, 0, 0));
        fxBlind = full(Colors.Black);
        layer.AddChild(fxInvert); layer.AddChild(fxFlash); layer.AddChild(fxBlind);
        AddChild(layer);
    }

    /// <summary>Colour + alpha of a "flash xlat" (official d3drender_fx.c table).</summary>
    static (Color c, float a) XlatFlash(uint xlat)
    {
        Color red = new Color(1, 0, 0), white = new Color(1, 1, 1), green = new Color(0, 1, 0), blue = new Color(0, 0, 1);
        if (xlat >= 0x41 && xlat <= 0x4A) return (red, Mathf.Min(255f, (xlat - 0x40) * 25f) / 255f);   // BLEND10..100RED: alpha 25,50,...,225,255
        return xlat switch
        {
            0x51 => (red, 75 / 255f), 0x57 => (red, 200 / 255f),
            0x70 => (white, 25 / 255f), 0x71 => (white, 50 / 255f), 0x72 => (white, 75 / 255f), 0x73 => (white, 100 / 255f), 0x74 => (white, 125 / 255f),
            0x75 => (white, 150 / 255f), 0x76 => (white, 175 / 255f), 0x77 => (white, 200 / 255f), 0x78 => (white, 225 / 255f), 0x79 => (white, 1f),
            0x53 => (green, 64 / 255f), 0x56 => (green, 128 / 255f), 0x59 => (green, 192 / 255f),
            0x52 => (blue, 64 / 255f), 0x55 => (blue, 128 / 255f), 0x58 => (blue, 192 / 255f),
            _ => (Colors.Black, 1f),
        };
    }

    void OnEffect(Effect ef)
    {
        if (AnimLog) GD.Print($"[effect] {ef.EffectType}");
        switch (ef)
        {
            case EffectPain p: fxPain = p.Duration > 10000 ? 10000 : p.Duration; break;
            case EffectWhiteOut w: fxWhiteout = w.Duration > 10000 ? 10000 : w.Duration; break;
            case EffectFlashXLat f:
                fxFlashMs = f.Duration > 10000 ? 1000 : f.Duration;
                var (c, a) = XlatFlash(f.XLat > 0xFF ? 0u : f.XLat); fxFlashColor = new Color(c.R, c.G, c.B, a);
                break;
            case EffectInvert i: fxInvertMs = i.Duration; break;
            case EffectShake s: fxShake = s.Duration; break;
            case EffectParalyze: fxParalyzed = true; AddChatLine("You are paralyzed!", "System"); break;
            case EffectRelease: fxParalyzed = false; break;
            case EffectBlind: fxBlindOn = true; break;
            case EffectSee: fxBlindOn = false; break;
            case EffectRaining: case EffectSnowing: case EffectClearWeather: case EffectSand: case EffectClearSand: OnWeatherEffect(ef); break;
            default: break;   // blur, waver: not drawn yet
        }
    }

    void UpdateScreenEffects(float dt)
    {
        if (fxFlash == null) return;
        float ms = dt * 1000f;
        if (fxPain > 0) fxPain = Mathf.Max(0, fxPain - ms);
        if (fxWhiteout > 0) fxWhiteout = Mathf.Max(0, fxWhiteout - ms);
        if (fxFlashMs > 0) fxFlashMs = Mathf.Max(0, fxFlashMs - ms);
        if (fxInvertMs > 0) fxInvertMs = Mathf.Max(0, fxInvertMs - ms);
        if (fxShake > 0) fxShake = Mathf.Max(0, fxShake - ms);

        // official: pain = red, alpha 204/255 * min(pain,2000)/2000; whiteout = white, alpha max(min(w,500)*255/500, 200)/255; flash = table colour
        Color col = new Color(0, 0, 0, 0);
        if (fxFlashMs > 0) col = fxFlashColor;
        if (fxWhiteout > 0) col = new Color(1, 1, 1, Mathf.Max(Mathf.Min(fxWhiteout, 500f) / 500f, 200f / 255f));
        if (fxPain > 0) { float a = 0.8f * Mathf.Min(fxPain, 2000f) / 2000f; col = new Color(1, 0, 0, Mathf.Max(a, col.A * 0.0f)); }
        fxFlash.Color = col; fxFlash.Visible = col.A > 0.001f;
        fxInvert.Visible = fxInvertMs > 0;
        fxBlind.Visible = fxBlindOn;
        if (cam != null)
        {
            float amp = fxShake > 0 ? Mathf.Min(fxShake / 3000f, 1f) * 0.12f : 0f;
            cam.HOffset = amp > 0 ? (GD.Randf() - 0.5f) * amp * 2f : 0f;
            cam.VOffset = amp > 0 ? (GD.Randf() - 0.5f) * amp * 2f : 0f;
        }
    }

    void ResetEffects()
    {
        fxPain = fxWhiteout = fxFlashMs = fxInvertMs = fxShake = 0; fxBlindOn = false; fxParalyzed = false;
        if (cam != null) { cam.HOffset = 0; cam.VOffset = 0; }
        ClearProjectiles(); ClearFloatTexts(); ResetWeather();
    }
}
