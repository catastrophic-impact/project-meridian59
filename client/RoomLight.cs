using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Room lighting, ported from the official Direct3D client (draw3d.c GetLightPaletteIndex / LightChanged3D, d3drender_world.c, d3drender_materials.c D3DRenderFogEndCalc):
/// every surface is lit by its sector's light plus the room's ambient light (which the server changes with the time of day) and the viewer's own light, optionally
/// scaled by the sun's direction (walls and steep slopes), and then fades to black with distance (linear fog, end depending on the same numbers).
/// Not done yet: lightmaps from light-emitting objects (torches, lamps...).
/// </summary>
public static class RoomLight
{
    public static float Dim;                                              // overcast 0..1 (weather): darkens the ambient light and weakens the sun
    public static float AmbientEff => Ambient * (1f - 0.5f * Dim);
    public static float Ambient = 255f, Viewer = 0f, ShadeAmount = 0f;   // ambient 0-255, viewer light 0-255, sun shade 0..1 (official intensity/64)
    public static Vector2 Sun = new Vector2(1, 0);                       // sun direction in world x (east) / y (south)

    public static readonly ImageTexture Purple = ImageTexture.CreateFromImage(Image.CreateFromData(1, 1, false, Image.Format.Rgba8, new byte[] { 140, 90, 153, 255 }));

    // ---- light sources (torches, lamps, spell glows): up to MaxLights nearest ones, uploaded as a 2-row float texture: row 0 = x,y,z,radius (Godot units), row 1 = r,g,b
    public const int MaxLights = 32;
    static Image lightImg; static ImageTexture lightTex; static int lightCount;
    public struct Light { public Vector3 Pos; public float Radius; public Color Col; }

    public static void SetLights(List<Light> lights)
    {
        if (lightImg == null) { lightImg = Image.CreateEmpty(MaxLights, 2, false, Image.Format.Rgbaf); lightTex = ImageTexture.CreateFromImage(lightImg); }
        lightCount = Math.Min(lights.Count, MaxLights);
        for (int i = 0; i < lightCount; i++)
        {
            var l = lights[i];
            lightImg.SetPixel(i, 0, new Color(l.Pos.X, l.Pos.Y, l.Pos.Z, l.Radius));
            lightImg.SetPixel(i, 1, new Color(l.Col.R, l.Col.G, l.Col.B, 1f));
        }
        lightTex.Update(lightImg);
        Apply();
    }

    static bool globalsDeclared;
    static void DeclareGlobals()
    {
        if (globalsDeclared) return; globalsDeclared = true;
        RenderingServer.GlobalShaderParameterAdd("m59_env", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(AmbientEff, Viewer, ShadeAmount * (1f - 0.7f * Dim), lightCount));
        RenderingServer.GlobalShaderParameterAdd("m59_sun", RenderingServer.GlobalShaderParameterType.Vec2, Sun);
        if (lightImg == null) { lightImg = Image.CreateEmpty(MaxLights, 2, false, Image.Format.Rgbaf); lightTex = ImageTexture.CreateFromImage(lightImg); }
        RenderingServer.GlobalShaderParameterAdd("m59_lights", RenderingServer.GlobalShaderParameterType.Sampler2D, lightTex);
    }

    /// <summary>Push the current values to every room material (global shader parameters).</summary>
    public static void Apply()
    {
        DeclareGlobals();
        RenderingServer.GlobalShaderParameterSet("m59_env", new Vector4(AmbientEff, Viewer, ShadeAmount * (1f - 0.7f * Dim), lightCount));
        RenderingServer.GlobalShaderParameterSet("m59_sun", Sun);
    }

    public static void SetDim(float d) { d = Mathf.Clamp(d, 0f, 1f); if (Mathf.Abs(d - Dim) < 0.004f) return; Dim = d; Apply(); }

    public static void Set(byte ambient, byte viewer)
    {
        Ambient = ambient; Viewer = viewer; Apply();
    }

    public static void SetSun(int intensity, int angle)
    {
        ShadeAmount = Mathf.Clamp(intensity / 64f, 0f, 1f);
        float a = angle / 4096f * Mathf.Tau;
        Sun = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        Apply();
    }

    // ---- the same maths on the CPU (used for sprites): brightness 0..1 of a surface in a sector of the given light, and the fog factor at a distance (fineness)
    public static int PaletteIndex(int sectorLight, float scale = 1f)
    {
        int idx;
        if (sectorLight > 127)
        {
            int rows = (int)Math.Min(255f, 8f * Viewer + AmbientEff);
            idx = (rows + sectorLight - 192) * 64 / 256;
            if (scale != 1f) idx = (int)(scale * idx);
        }
        else idx = (int)Math.Min(255f, 16f * Viewer) * 64 / 256 + sectorLight / 2;
        return Math.Clamp(idx, 0, 63);
    }
    public static float Brightness(int sectorLight, float scale = 1f) => PaletteIndex(sectorLight, scale) * 239f / 64f / 255f;
    public static float FogEnd(int sectorLight) => sectorLight <= 127 ? 16384f + sectorLight * 1024f + Viewer * 64f
        : 32768f + Math.Max(0, sectorLight - 192) * 1024f + Viewer * 64f + AmbientEff * 1024f;
    public static float Fog(int sectorLight, float distFineness) => Mathf.Clamp(1f - distFineness / FogEnd(sectorLight), 0f, 1f);

    // ---- shader
    const string Code = @"shader_type spatial;
render_mode unshaded, CULLMODE;
global uniform vec4 m59_env;
global uniform vec2 m59_sun;
global uniform sampler2D m59_lights : filter_nearest;
uniform sampler2D tex : source_color, filter_nearest_mipmap, REPEATMODE;
uniform vec2 uv_off = vec2(0.0);
uniform bool cutout = false;
varying float bright;
varying float fog_end;
varying vec3 wpos;
varying vec3 nrm;
void vertex() {
    wpos = VERTEX;
    nrm = NORMAL;
    float L = floor(COLOR.r * 255.0 + 0.5);
    float amb = m59_env.x;
    float view = m59_env.y;
    float idx;
    if (L > 127.0) {
        float rows = min(255.0, 8.0 * view + amb);
        idx = trunc((rows + L - 192.0) * 64.0 / 256.0);
        if (COLOR.b > 0.5 && m59_env.z > 0.0) {
            float d = NORMAL.x * m59_sun.x + NORMAL.z * m59_sun.y;
            float sc = clamp((1.0 - m59_env.z) + (d + 1.0) * 0.5 * m59_env.z, 0.0, 1.0);
            idx = floor(sc * idx);
        }
        fog_end = 32768.0 + max(0.0, L - 192.0) * 1024.0 + view * 64.0 + amb * 1024.0;
    } else {
        idx = floor(min(255.0, 16.0 * view) * 64.0 / 256.0) + floor(L / 2.0);
        fog_end = 16384.0 + L * 1024.0 + view * 64.0;
    }
    idx = clamp(idx, 0.0, 63.0);
    bright = idx * 239.0 / 64.0 / 255.0;
}
void fragment() {
    vec4 t = texture(tex, UV + uv_off);
    if (cutout && t.a < 0.5) discard;
    float depth = -VERTEX.z * 256.0;
    vec3 add = vec3(0.0);
    int n = int(m59_env.w);
    vec3 an = abs(normalize(nrm));
    for (int i = 0; i < n; i++) {
        vec4 a = texelFetch(m59_lights, ivec2(i, 0), 0);
        vec3 c = texelFetch(m59_lights, ivec2(i, 1), 0).rgb;
        vec3 d = wpos - a.xyz;
        float fall; float r;
        if (an.y >= an.x && an.y >= an.z) { fall = abs(d.y); r = length(d.xz); }
        else if (an.x > an.z) { fall = abs(d.x); r = length(d.yz); }
        else { fall = abs(d.z); r = length(d.xy); }
        add += c * (1.0 - min(1.0, fall / a.w)) * max(0.0, 1.0 - 2.0 * r / a.w);
    }
    ALBEDO = t.rgb * (bright * clamp(1.0 - depth / fog_end, 0.0, 1.0) + add);
}";
    static readonly Dictionary<int, Shader> shaders = new Dictionary<int, Shader>();
    public static Shader GetShader(bool wall, bool repeat)
    {
        int k = (wall ? 1 : 0) | (repeat ? 2 : 0);
        if (!shaders.TryGetValue(k, out var sh))
        {
            DeclareGlobals();
            sh = new Shader { Code = Code.Replace("CULLMODE", wall ? "cull_back" : "cull_disabled").Replace("REPEATMODE", repeat ? "repeat_enable" : "repeat_disable") };
            shaders[k] = sh;
        }
        return sh;
    }
}
