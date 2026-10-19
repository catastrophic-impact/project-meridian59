using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;

/// <summary>
/// Builds the picture of an object that has overlays (players: torso + arms + legs + head with face parts + hair, and any
/// worn equipment) from the viewer's angle. Rules are ported from the official client (clientd3d/object3d.c FindHotspot,
/// drawbmp.c, ComputeObjectArea):
///  - an overlay with hotspot number N is attached at the base bitmap's hotspot N (+ the overlay's own bitmap offset);
///  - if the base has no such hotspot, another overlay (e.g. the head) that has it is used, chained via that overlay's own attachment;
///  - a negative hotspot number in the host bitmap means "draw behind the host";
///  - the frame shown depends on (object angle - viewer angle - 3072) and the frame set of the bitmap group;
///  - the base bitmap's bottom edge rests at floor height minus its YOffset, centred on the object plus its XOffset.
/// All layer maths is in "base pixels"; each layer is scaled by baseShrink/layerShrink (shrink differs per BGF).
/// </summary>
public static class AvatarComposer
{
    public class Result
    {
        public ImageTexture Texture;
        public float PixelSize;          // Godot units per output pixel
        public Vector2 Offset;           // Sprite3D offset (output pixels, +y up) for a centred sprite whose origin is on the floor
        public float Top;                // height of the picture's top edge above the floor, Godot units
    }

    class Loaded { public BgfBitmap Bmp; public byte[] Pix; public int W, H, Shrink; }
    class Layer { public byte[] Rgba; public int W, H; public float X, Y, Scale; public int Depth; }

    // depth classes in drawing order (official: UNDERUNDER, UNDER, UNDEROVER, [base], OVERUNDER, OVER, OVEROVER)
    const int UnderUnder = 0, Under = 1, UnderOver = 2, Base = 3, OverUnder = 4, Over = 5, OverOver = 6;

    static readonly Dictionary<string, BgfFile> files = new Dictionary<string, BgfFile>();

    static BgfFile File(string path)
    {
        if (!files.TryGetValue(path, out var f))
        {
            try { f = new BgfFile(path); } catch (Exception ex) { GD.PrintErr("bgf " + path + ": " + ex.Message); f = null; }
            files[path] = f;
        }
        return f;
    }

    static Loaded Load(string path, int group, ushort angle)
    {
        var bgf = path == null ? null : File(path);
        if (bgf == null || bgf.Frames.Count == 0) return null;
        var bmp = bgf.GetFrame(Math.Max(1, group), angle) ?? bgf.Frames[0];
        if (bmp.IsCompressed) bmp.IsCompressed = false;
        int w = (int)bmp.Width, h = (int)bmp.Height;
        if (w <= 0 || h <= 0 || bmp.PixelData == null || bmp.PixelData.Length < w * h) return null;
        int shrink = (int)bgf.ShrinkFactor; if (shrink <= 0) shrink = 1;
        return new Loaded { Bmp = bmp, Pix = bmp.PixelData, W = w, H = h, Shrink = shrink };
    }

    static byte[] ToRgba(Loaded l, byte translation)
    {
        uint[] pal = translation == 0 ? ColorTransformation.DefaultPalette : ColorTransformation.Palettes[Math.Min((int)translation, ColorTransformation.Palettes.Length - 1)];
        var rgba = new byte[l.W * l.H * 4];
        for (int i = 0; i < l.W * l.H; i++)
        {
            byte idx = l.Pix[i];
            uint c = pal[idx];
            rgba[i * 4] = (byte)(c >> 16); rgba[i * 4 + 1] = (byte)(c >> 8); rgba[i * 4 + 2] = (byte)c;
            rgba[i * 4 + 3] = idx == ColorTransformation.TRANSPARENTCOLORINDEX ? (byte)0 : (byte)255;
        }
        return rgba;
    }

    static bool FindHotspot(BgfBitmap b, int number, out float x, out float y, out bool over)
    {
        foreach (var h in b.HotSpots)
            if (Math.Abs(h.Index) == number) { x = h.X; y = h.Y; over = h.Index > 0; return true; }
        x = y = 0; over = true; return false;
    }

    /// <param name="angle">relative view angle 0..4095 (see RelativeAngle)</param>
    /// <summary>Current bitmap group of an animation (none = its fixed group; cycle/once = the group the animation has reached).</summary>
    public static ushort GroupOf(Meridian59.Data.Models.Animation a)
    {
        ushort g = a switch { AnimationNone n => n.Group, null => (ushort)1, _ => a.CurrentGroup };
        return g == 0 ? (ushort)1 : g;
    }

    /// <param name="weaponsOverBody">seen from behind: a weapon swung in front of the body would be hidden by the torso; draw weapons (hotspots 22, 32, 33) on top instead</param>
    /// <param name="motion">use the object's movement animation + movement overlays (it is walking) instead of the standing ones</param>
    public static Result Compose(RoomObject o, ushort angle, Func<uint, string> resolveFile, bool motion = false, bool weaponsOverBody = false)
    {
        if (motion && o.MotionAnimation == null) motion = false;
        ushort baseGroup = GroupOf(motion ? o.MotionAnimation : o.Animation);
        var bas = Load(resolveFile(o.OverlayFileRID), baseGroup, angle);
        if (bas == null) return null;
        byte baseTranslation = motion && o.MotionColorTranslation != 0 ? o.MotionColorTranslation : o.ColorTranslation;

        // load every overlay's frame for this angle
        var ovs = new List<(SubOverlay so, Loaded ld)>();
        foreach (var so in motion ? o.MotionSubOverlays : o.SubOverlays)
        {
            if (so.ResourceID == 0) continue;
            ushort g = GroupOf(so.Animation);
            var ld = Load(resolveFile(so.ResourceID), g, angle);
            if (ld != null) ovs.Add((so, ld));
        }

        bool hasBow = false;                                         // bows use hotspots 32 + 33, shields only 32
        foreach (var (so0, _) in ovs) if (so0.HotSpot == 33) hasBow = true;
        float baseShrink = bas.Shrink;
        var layers = new List<Layer> { new Layer { Rgba = ToRgba(bas, baseTranslation), W = bas.W, H = bas.H, X = 0, Y = 0, Scale = 1f, Depth = Base } };
        float maxRes = 1f;

        foreach (var (so, ld) in ovs)
        {
            float x, y; int depth;
            float scale = baseShrink / ld.Shrink;                       // overlay pixel size in base pixels
            if (so.HotSpot == 0) { x = 0; y = 0; depth = Over; }
            else if (FindHotspot(bas.Bmp, so.HotSpot, out float hx, out float hy, out bool hover))
            {
                x = hx + ld.Bmp.XOffset; y = hy + ld.Bmp.YOffset;
                depth = hover ? Over : Under;
            }
            else
            {
                bool placed = false; x = y = 0; depth = Over;
                foreach (var (so2, ld2) in ovs)
                {
                    if (!FindHotspot(ld2.Bmp, so.HotSpot, out float h2x, out float h2y, out bool over2)) continue;
                    if (FindHotspot(bas.Bmp, so2.HotSpot, out float p2x, out float p2y, out bool over1))
                    {
                        float k2 = baseShrink / ld2.Shrink;
                        x = h2x * k2 + p2x + ld2.Bmp.XOffset + ld.Bmp.XOffset * k2;
                        y = h2y * k2 + p2y + ld2.Bmp.YOffset + ld.Bmp.YOffset * k2;
                        depth = over2 ? (over1 ? OverOver : UnderOver) : (over1 ? OverUnder : UnderUnder);
                    }
                    else { x = h2x; y = h2y; depth = Over; }
                    placed = true; break;
                }
                if (!placed) continue;
            }
            if (weaponsOverBody && depth < Base && (so.HotSpot == 22 || so.HotSpot == 33 || (so.HotSpot == 32 && hasBow))) depth = Over;   // a lone hotspot-32 overlay is a shield: it stays behind the body
            maxRes = Math.Max(maxRes, 1f / scale);
            layers.Add(new Layer { Rgba = ToRgba(ld, so.ColorTranslation), W = ld.W, H = ld.H, X = x, Y = y, Scale = scale, Depth = depth });
        }

        int kres = Math.Max(1, (int)Math.Ceiling(maxRes));
        float minX = 0, minY = 0, maxX = bas.W, maxY = bas.H;
        foreach (var l in layers) { minX = Math.Min(minX, l.X); minY = Math.Min(minY, l.Y); maxX = Math.Max(maxX, l.X + l.W * l.Scale); maxY = Math.Max(maxY, l.Y + l.H * l.Scale); }
        int cw = (int)Math.Ceiling((maxX - minX) * kres), ch = (int)Math.Ceiling((maxY - minY) * kres);
        if (cw <= 0 || ch <= 0 || cw > 2048 || ch > 2048) return null;
        var canvas = new byte[cw * ch * 4];

        // draw in depth order (stable within a class = server order)
        for (int d = UnderUnder; d <= OverOver; d++)
            foreach (var l in layers)
            {
                if (l.Depth != d) continue;
                int x0 = Math.Max(0, (int)Math.Floor((l.X - minX) * kres)), x1 = Math.Min(cw, (int)Math.Ceiling((l.X + l.W * l.Scale - minX) * kres));
                int y0 = Math.Max(0, (int)Math.Floor((l.Y - minY) * kres)), y1 = Math.Min(ch, (int)Math.Ceiling((l.Y + l.H * l.Scale - minY) * kres));
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        float bx = x / (float)kres + minX, by = y / (float)kres + minY;        // position in base pixels
                        int sx = (int)Math.Floor((bx - l.X) / l.Scale), sy = (int)Math.Floor((by - l.Y) / l.Scale);
                        if (sx < 0 || sy < 0 || sx >= l.W || sy >= l.H) continue;
                        int s = (sy * l.W + sx) * 4;
                        if (l.Rgba[s + 3] == 0) continue;
                        int dst = (y * cw + x) * 4;
                        canvas[dst] = l.Rgba[s]; canvas[dst + 1] = l.Rgba[s + 1]; canvas[dst + 2] = l.Rgba[s + 2]; canvas[dst + 3] = 255;
                    }
            }

        var img = Image.CreateFromData(cw, ch, false, Image.Format.Rgba8, canvas);
        float pixel = 16f * RoomBuilder.S / baseShrink;                 // Godot units per BASE pixel
        // base bitmap centre sits at the object centre + XOffset; its bottom edge at floor - YOffset (YOffset < 0 lifts the torso)
        float centerX = minX + cw / (2f * kres), centerY = minY + ch / (2f * kres);
        float dxBase = centerX - bas.W / 2f + bas.Bmp.XOffset;
        float upBase = (bas.H - centerY) - bas.Bmp.YOffset;
        return new Result
        {
            Texture = ImageTexture.CreateFromImage(img),
            PixelSize = pixel / kres,
            Offset = new Vector2(dxBase * kres, upBase * kres),
            Top = ((bas.H - minY) - bas.Bmp.YOffset) * pixel,
        };
    }

    /// <summary>
    /// Frame-selection angle: 0 = we see the object's front (it faces us), 2048 = its back (same facing as the viewer), 1024 = profile facing our left,
    /// 3072 = profile facing our right. Verified by rendering one character at all 8 angles (M59_AVATAR_SHEET). Both angles use the server convention
    /// (0 = east, growing clockwise seen from above). (The official client writes this as object - (viewer + 3072); that expression gives a profile
    /// for "same facing", so its convention must differ from ours - the geometry above is what matters.)
    /// </summary>
    public static ushort RelativeAngle(int objectAngle, int viewerAngle) => (ushort)((((objectAngle - viewerAngle + 2048) % 4096) + 4096) % 4096);
}
