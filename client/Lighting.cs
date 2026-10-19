using Godot;

/// <summary>Light on the object sprites (official D3DObjectLightingCalc): brightness from the sector light under the object, faded with distance like the world.</summary>
public partial class Main
{
    double spriteLightAt; int lastLightLog = -1;

    void UpdateSpriteLight()
    {
        if (currentRoom == null || walker == null || objSprites.Count == 0) return;
        double now = Time.GetTicksMsec();
        if (now < spriteLightAt) return;
        spriteLightAt = now + 100;
        foreach (var kv in objSprites)
        {
            if (!IsInstanceValid(kv.Value)) continue;
            float x, y;
            if (kv.Key == client.PlayerId) { x = walker.X; y = walker.Y; }
            else if (objPos.TryGetValue(kv.Key, out var p)) { x = p.x; y = p.y; }
            else continue;
            int sn = currentRoom.FindSector(x, y);
            int light = sn > 0 ? currentRoom.Sectors[sn - 1].Light : 192;
            float dx = x - walker.X, dy = y - walker.Y;
            float b = RoomLight.Brightness(light) * RoomLight.Fog(light, Mathf.Sqrt(dx * dx + dy * dy));
            float m = Mathf.Clamp(b, 0f, 1f);
            // the nearest light source adds its colour (official UpdateClosestLight / D3DObjectLightingCalc)
            Color add = new Color(0, 0, 0);
            float best = 18000f, oz = FloorAt(x, y);
            foreach (var ls in lightSrc)
            {
                float lx = x - ls.rawX, ly = y - ls.rawY, lz = oz - ls.rawZ;
                float ratio = Mathf.Sqrt(lx * lx + ly * ly + lz * lz) / (ls.radiusFineness / 2f);
                if (ratio < best) { best = ratio; float k = Mathf.Max(0f, 1f - ratio) * 239f / 255f; add = new Color(ls.col.R * k, ls.col.G * k, ls.col.B * k); }
            }
            kv.Value.Modulate = new Color(Mathf.Min(239f / 255f, m + add.R), Mathf.Min(239f / 255f, m + add.G), Mathf.Min(239f / 255f, m + add.B), kv.Value.Modulate.A);
        }
    }
}

public partial class Main
{
    readonly System.Collections.Generic.List<RoomLight.Light> lightList = new System.Collections.Generic.List<RoomLight.Light>();
    readonly System.Collections.Generic.List<(float d, RoomLight.Light l, float rawX, float rawY, float rawZ, float radiusFineness, Color col)> lightSrc = new System.Collections.Generic.List<(float, RoomLight.Light, float, float, float, float, Color)>();

    /// <summary>Gather the light sources of the room's objects (official D3DLMapsStaticGet): radius from the intensity, colour from the 5-5-5 colour, flicker/flash modulate both.</summary>
    void UpdateLights()
    {
        if (currentRoom == null || walker == null) return;
        lightSrc.Clear();
        double t = Time.GetTicksMsec() / 1000.0;
        foreach (var o in client.Objects)
        {
            if (o.LightFlags == 0 || o.LightColor == 0 || o.LightIntensity == 0) continue;
            float x, y;
            if (o.ID == client.PlayerId) { x = walker.X; y = walker.Y; }
            else if (objPos.TryGetValue(o.ID, out var p)) { x = p.x; y = p.y; }
            else continue;
            bool dynamicFlag = (o.LightFlags & 2) != 0;
            float intensity = o.LightIntensity * (dynamicFlag ? 10f : 1f);
            bool flick = o.Flags.IsFlickering && !o.Flags.IsFlashing, flash = o.Flags.IsFlashing && !o.Flags.IsFlickering;
            float fb = 1f, radius;
            if (flick) fb = FlickerBrightness(t + o.ID * 0.37);
            else if (flash) fb = Mathf.Max(0f, Mathf.Sin((float)((t + o.ID * 0.37) % 1.0 * Mathf.Tau)));
            radius = (flick || flash) ? (intensity * 12000f / 255f + 2000f) * 0.45f * fb : intensity * 14000f / 255f + 4000f;
            if (radius <= 1f) continue;
            ushort c = o.LightColor;
            var col = new Color(((c >> 10) & 31) / 31f * fb, ((c >> 5) & 31) / 31f * fb, (c & 31) / 31f * fb);
            // height: the sprite's own height (at least 64) above the floor
            float floor = FloorAt(x, y), h = 64f;
            var path = ResolveFile(client.Resolve(o.OverlayFileRID));
            if (path != null)
            {
                ushort g = (o.Animation is Meridian59.Data.Models.AnimationNone an) ? an.Group : (ushort)1; if (g == 0) g = 1;
                var e = sprites.Get(path, g, 0, o.ColorTranslation);
                if (e != null) h = Mathf.Max(64f, e.Height / (float)e.Shrink * 16f - e.YOffset * 4f);
            }
            float dx = x - walker.X, dy = y - walker.Y;
            var l = new RoomLight.Light { Pos = RoomBuilder.P(x, y, floor + h), Radius = radius * RoomBuilder.S, Col = col };
            lightSrc.Add((dx * dx + dy * dy, l, x, y, floor + h, radius, col));
        }
        if (System.Environment.GetEnvironmentVariable("M59_TESTLIGHT") == "1")
        {   // test: a strong white light 1 square in front of the player
            float fx = -Mathf.Sin(yaw) * 1024f, fy = -Mathf.Cos(yaw) * 1024f;
            var tl = new RoomLight.Light { Pos = RoomBuilder.P(walker.X + fx, walker.Y + fy, walker.Z + 400f), Radius = 8000f * RoomBuilder.S, Col = new Color(1, 0.8f, 0.3f) };
            lightSrc.Add((0f, tl, walker.X + fx, walker.Y + fy, walker.Z + 400f, 8000f, tl.Col));
        }
        lightSrc.Sort((a, b) => a.d.CompareTo(b.d));
        lightList.Clear();
        foreach (var s in lightSrc) { lightList.Add(s.l); if (lightList.Count >= RoomLight.MaxLights) break; }
        if (AnimLog && lightList.Count != lastLightLog) { lastLightLog = lightList.Count; GD.Print($"[light] {lightList.Count} light sources"); foreach (var q in lightSrc) GD.Print($"[light]   at {q.rawX:F0},{q.rawY:F0},{q.rawZ:F0} radius {q.radiusFineness:F0} colour {q.col}"); }
        RoomLight.SetLights(lightList);
    }

    // official animate.c: five sine waves, compressed to 70..100 %
    static float FlickerBrightness(double t)
    {
        float[] amp = { 1.3f, 1.1f, 0.9f, 1.4f, 1.2f }, fr = { 3.14f, 7.0f, 11.0f, 2.0f, 0.62f };
        float sum = 0f, f = 0f;
        for (int i = 0; i < 5; i++) { f += Mathf.Sin((float)t * fr[i]) * amp[i]; sum += amp[i]; }
        f = Mathf.Clamp(f / sum + 0.5f, 0f, 1f);
        return 0.70f + 0.30f * f;
    }
}
