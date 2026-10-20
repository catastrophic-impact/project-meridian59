using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>Skybox (BP_BACKGROUND). The retail skyNNN.bsf files are six 512x512 PNGs appended to each other (Back, Bottom, Front, Left, Right, Top).
/// Ported from the official d3drender_skybox.c: a cube of half-width 75000 and half-height 37000 fineness centred on the camera, drawn first without depth,
/// "1skya.bgf" style names from the server map to skya.bsf etc.</summary>
public partial class Main
{
    Node3D skyNode; MeshInstance3D skyMesh; string skyShown;
    readonly Dictionary<string, Texture2D[]> skyCache = new Dictionary<string, Texture2D[]>();

    static string SkyFileFor(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string n = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        if (n.Length > 1 && char.IsDigit(n[0])) n = n.Substring(1);        // 1skya / 2skya -> skya
        return n + ".bsf";
    }

    Texture2D[] LoadSky(string bsf)
    {
        if (skyCache.TryGetValue(bsf, out var t)) return t;
        t = null;
        try
        {
            var path = ResolveFile(bsf);
            if (path != null)
            {
                var d = File.ReadAllBytes(path);
                var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                var starts = new List<int>();
                for (int i = 0; i + 8 <= d.Length; i++)
                {
                    bool ok = true; for (int k = 0; k < 8; k++) if (d[i + k] != sig[k]) { ok = false; break; }
                    if (ok) starts.Add(i);
                }
                if (starts.Count >= 6)
                {
                    t = new Texture2D[6];
                    for (int f = 0; f < 6; f++)
                    {
                        int a = starts[f], b = f + 1 < starts.Count ? starts[f + 1] : d.Length;
                        var buf = new byte[b - a]; Array.Copy(d, a, buf, 0, b - a);
                        var img = new Image(); img.LoadPngFromBuffer(buf);
                        t[f] = ImageTexture.CreateFromImage(img);
                    }
                }
            }
        }
        catch (Exception ex) { GD.PrintErr("[sky] " + ex.Message); t = null; }
        skyCache[bsf] = t;
        return t;
    }

    // the sky picture, greyer and darker when it is overcast, with a flash for lightning (see Weather.cs)
    static readonly Shader SkyShader = new Shader { Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;
uniform sampler2D tex : source_color, filter_linear, repeat_disable;
uniform float overcast = 0.0;
uniform float flash = 0.0;
void fragment() {
    vec3 c = texture(tex, UV).rgb;
    float l = dot(c, vec3(0.30, 0.59, 0.11));
    vec3 grey = vec3(l * 0.95 + 0.03) * vec3(0.80, 0.84, 0.92);
    c = mix(c, grey, clamp(overcast * 1.05, 0.0, 1.0));
    c *= 1.0 - 0.32 * overcast;
    c += vec3(0.9, 0.95, 1.0) * flash;
    ALBEDO = c;
}" };

    void SetSkyWeather(float overcast, float flash)
    {
        if (skyMesh?.Mesh == null) return;
        for (int f = 0; f < skyMesh.Mesh.GetSurfaceCount(); f++)
            if (skyMesh.Mesh.SurfaceGetMaterial(f) is ShaderMaterial m) { m.SetShaderParameter("overcast", overcast); m.SetShaderParameter("flash", flash); }
    }

    void BuildSkyNode()
    {
        skyNode = new Node3D { Name = "Sky", Visible = false };
        skyMesh = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 16384f };
        skyNode.AddChild(skyMesh);
        AddChild(skyNode);
    }

    void SetBackground(string name)
    {
        if (skyNode == null) BuildSkyNode();
        string bsf = SkyFileFor(name);
        if (bsf == null) { skyNode.Visible = false; skyShown = null; return; }
        if (bsf == skyShown) { skyNode.Visible = true; return; }
        var tex = LoadSky(bsf);
        if (tex == null) { GD.Print($"[sky] no sky picture for '{name}' ({bsf})"); skyNode.Visible = false; skyShown = null; return; }

        const float SkyScale = 4f;   // the cube is centred on the camera, so its size does not change the picture; big enough that no room geometry ends up behind it (camera far plane is 2000)
        float D = 75000f * RoomBuilder.S * SkyScale, H = 37000f * RoomBuilder.S * SkyScale;       // table of the official client is already (x, up, +y south) = our Godot layout
        Vector3[] v =
        {
            new(D, H, -D), new(D, -H, -D), new(-D, -H, -D), new(-D, H, -D),      // back
            new(-D, -H, D), new(-D, -H, -D), new(D, -H, -D), new(D, -H, D),      // bottom
            new(-D, H, D), new(-D, -H, D), new(D, -H, D), new(D, H, D),          // front
            new(-D, H, -D), new(-D, -H, -D), new(-D, -H, D), new(-D, H, D),      // left
            new(D, H, D), new(D, -H, D), new(D, -H, -D), new(D, H, -D),          // right
            new(-D, H, -D), new(-D, H, D), new(D, H, D), new(D, H, -D),          // top
        };
        Vector2[] uv = { new(0.001f, 0.001f), new(0.001f, 0.999f), new(0.999f, 0.999f), new(0.999f, 0.001f) };
        var mesh = new ArrayMesh();
        for (int f = 0; f < 6; f++)
        {
            var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = new[] { v[f * 4], v[f * 4 + 1], v[f * 4 + 2], v[f * 4 + 3] };
            arr[(int)Mesh.ArrayType.TexUV] = uv;
            arr[(int)Mesh.ArrayType.Index] = new[] { 1, 2, 0, 2, 3, 0 };
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            var sm = new ShaderMaterial { Shader = SkyShader, RenderPriority = -100 };
            sm.SetShaderParameter("tex", tex[f]);
            mesh.SurfaceSetMaterial(f, sm);
        }
        skyMesh.Mesh = mesh;
        SetSkyWeather(wxOvercast, wxFlash);
        skyShown = bsf; skyNode.Visible = true;
        GD.Print($"[sky] {bsf}");
    }

    // ---- background overlays (sun, moon...): official d3drender_bgoverlays.c - sprite on a ring of radius 1000 fineness around the viewer at the given angle,
    // height -200..200 fineness relative to the eye, size = bitmap * 1.2 fineness; always in front of the room geometry beyond that tiny radius.
    class BgOv { public Sprite3D S; public int Angle, Height; public string Path; public Meridian59.Data.Models.Animation Anim; public int Key = int.MinValue; }
    const float OvScale = 230f;     // 1000 fineness = 3.9 units -> about 900 units (room sizes are under 300, camera far plane 2000)
    readonly Dictionary<uint, BgOv> bgOverlays = new Dictionary<uint, BgOv>();

    void OnBackgroundOverlay(Meridian59.Data.Models.BackgroundOverlay b)
    {
        if (objectsRoot == null) return;
        if (bgOverlays.Remove(b.ID, out var old)) old.S.QueueFree();
        string path = ResolveFile(client.Resolve(b.NameRID)) ?? ResolveFile(client.Resolve(b.OverlayFileRID));   // (the library's two resource fields are the other way round for these)
        if (path == null) { GD.Print($"[sky] overlay {b.ID}: no picture for file rid {b.OverlayFileRID} = '{client.Resolve(b.OverlayFileRID)}' (name rid {b.NameRID} = '{client.Resolve(b.NameRID)}')"); return; }
        var ov = new BgOv
        {
            Angle = b.Angle, Height = b.Height, Path = path, Anim = b.Animation,
            S = new Sprite3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, DoubleSided = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                               AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Name = "bgoverlay" + b.ID },
        };
        AddChild(ov.S);
        bgOverlays[b.ID] = ov;
        GD.Print($"[sky] overlay {b.ID} {client.Resolve(b.OverlayFileRID)} angle {b.Angle} height {b.Height}");
    }

    void ClearBgOverlays() { foreach (var o in bgOverlays.Values) if (IsInstanceValid(o.S)) o.S.QueueFree(); bgOverlays.Clear(); }

    void UpdateBgOverlays()
    {
        if (bgOverlays.Count == 0 || cam == null) return;
        double now = Time.GetTicksMsec();
        foreach (var o in bgOverlays.Values)
        {
            var anim = o.Anim;
            if (anim != null && !(anim is Meridian59.Data.Models.AnimationNone)) { anim.GroupMax = sprites.Groups(o.Path); anim.Tick(now, 0); }
            int key = AvatarComposer.GroupOf(anim);
            if (key != o.Key)
            {
                o.Key = key;
                var e = sprites.Get(o.Path, key, 0);
                if (e != null) { o.S.Texture = e.Texture; o.S.PixelSize = 1.2f * RoomBuilder.S * OvScale; o.S.Offset = new Vector2(0, e.Height / 2f); }
            }
            const float Radius = 1000f;
            float h = Mathf.Clamp(o.Height, -200, 200);
            float hr = Mathf.Sqrt(Radius * Radius - h * h);
            float a = o.Angle / 4096f * Mathf.Tau;
            // kod angle: 0 = east, growing clockwise seen from above = toward south (+y) = Godot +Z
            // The official client draws these behind everything. Here they sit on the same direction but OvScale times further out, beyond all room geometry, so walls/mountains
            // correctly hide them, they do not parallax when the player walks, and nothing can poke through them.
            o.S.GlobalPosition = cam.GlobalPosition + new Vector3(hr * Mathf.Cos(a), h, hr * Mathf.Sin(a)) * RoomBuilder.S * OvScale;
            if (AnimLog && Engine.GetProcessFrames() % 120 == 0) GD.Print($"[sky] {o.S.Name} screen {(cam.IsPositionBehind(o.S.GlobalPosition) ? "behind" : cam.UnprojectPosition(o.S.GlobalPosition).ToString())}");
        }
    }

    /// <summary>The sky is infinitely far: keep it centred on the camera.</summary>
    void UpdateSky()
    {
        if (skyNode != null && skyNode.Visible && cam != null) skyNode.GlobalPosition = cam.GlobalPosition;
        UpdateBgOverlays();
    }
}
