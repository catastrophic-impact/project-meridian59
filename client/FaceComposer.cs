using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;

/// <summary>
/// Builds a character's face portrait the way the original attaches overlays. The head bitmap (overlay hotspot 1) has
/// numbered hotspots; eyes (11), mouth (12), hair (13) and nose (14) are placed at the head's hotspot of the same number
/// plus the part's own x/y offset (both in head-pixel units). Bitmaps have different "shrink factors" (resolution): a part
/// is drawn at headShrink/partShrink of its pixel size. Negative hotspot index = under the head. Skin and hair colours
/// are palette translations.
/// </summary>
public static class FaceComposer
{
    class Layer { public byte[] Rgba; public int W, H; public float X, Y, Scale; public bool Under; }

    public static ImageTexture Compose(RoomObject player, Func<uint, string> resolveFile)
    {
        SubOverlay head = null;
        var parts = new List<SubOverlay>();
        foreach (var so in player.SubOverlays)
        {
            if (so.ResourceID == 0) continue;
            if (so.HotSpot == 1) head = so;
            else if (so.HotSpot >= 11 && so.HotSpot <= 14) parts.Add(so);
        }
        if (head == null) return null;

        var (headBmp, headRgba, headShrink) = Load(head, resolveFile);
        if (headBmp == null) return null;

        var layers = new List<Layer> { new Layer { Rgba = headRgba, W = (int)headBmp.Width, H = (int)headBmp.Height, X = 0, Y = 0, Scale = 1f } };
        float maxRes = 1f;                                    // finest part resolution relative to the head (for the output size)
        foreach (var so in parts)
        {
            var (bmp, rgba, shrink) = Load(so, resolveFile);
            if (bmp == null) continue;
            BgfBitmapHotspot hs = null;
            foreach (var h in headBmp.HotSpots) if (Math.Abs(h.Index) == so.HotSpot) { hs = h; break; }
            if (hs == null) continue;
            float scale = (float)headShrink / shrink;         // part pixel size in head pixels
            maxRes = Math.Max(maxRes, 1f / scale);
            layers.Add(new Layer { Rgba = rgba, W = (int)bmp.Width, H = (int)bmp.Height, X = hs.X + bmp.XOffset, Y = hs.Y + bmp.YOffset, Scale = scale, Under = hs.Index < 0 });
        }

        int k = Math.Max(1, (int)Math.Ceiling(maxRes));        // output pixels per head pixel
        float minX = 0, minY = 0, maxX = layers[0].W, maxY = layers[0].H;
        foreach (var l in layers) { minX = Math.Min(minX, l.X); minY = Math.Min(minY, l.Y); maxX = Math.Max(maxX, l.X + l.W * l.Scale); maxY = Math.Max(maxY, l.Y + l.H * l.Scale); }
        int cw = (int)Math.Ceiling((maxX - minX) * k), ch = (int)Math.Ceiling((maxY - minY) * k);
        if (cw <= 0 || ch <= 0 || cw > 2048 || ch > 2048) return null;
        var canvas = new byte[cw * ch * 4];

        void blit(Layer l)
        {
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    float hx = x / (float)k + minX, hy = y / (float)k + minY;      // position in head pixels
                    int sx = (int)Math.Floor((hx - l.X) / l.Scale), sy = (int)Math.Floor((hy - l.Y) / l.Scale);
                    if (sx < 0 || sy < 0 || sx >= l.W || sy >= l.H) continue;
                    int s = (sy * l.W + sx) * 4;
                    if (l.Rgba[s + 3] == 0) continue;
                    int d = (y * cw + x) * 4;
                    canvas[d] = l.Rgba[s]; canvas[d + 1] = l.Rgba[s + 1]; canvas[d + 2] = l.Rgba[s + 2]; canvas[d + 3] = 255;
                }
        }
        foreach (var l in layers) if (l.Under) blit(l);                               // behind the head
        blit(layers[0]);                                                              // the head
        foreach (var l in layers) if (!l.Under && l != layers[0]) blit(l);            // eyes, nose, mouth, hair on top

        var img = Image.CreateFromData(cw, ch, false, Image.Format.Rgba8, canvas);
        if (System.Environment.GetEnvironmentVariable("M59_FACE_DEBUG") != null)
        {
            var big = (Image)img.Duplicate(); big.Resize(cw * 3, ch * 3, Image.Interpolation.Nearest);
            big.SavePng(System.Environment.GetEnvironmentVariable("M59_FACE_DEBUG"));
        }
        return ImageTexture.CreateFromImage(img);
    }

    static (BgfBitmap, byte[], int) Load(SubOverlay so, Func<uint, string> resolveFile)
    {
        try
        {
            string path = resolveFile(so.ResourceID);
            if (path == null) return (null, null, 1);
            var bgf = new BgfFile(path);
            if (bgf.Frames.Count == 0) return (null, null, 1);
            ushort grp = (so.Animation as AnimationNone)?.Group ?? (ushort)1;
            var bmp = bgf.GetFrame(Math.Max(1, (int)grp), 0) ?? bgf.Frames[0];   // front view = angle 0 (hotspots are centred)
            if (bmp.IsCompressed) bmp.IsCompressed = false;
            int w = (int)bmp.Width, h = (int)bmp.Height;
            var src = bmp.PixelData;
            if (w <= 0 || h <= 0 || src == null || src.Length < w * h) return (null, null, 1);

            uint[] pal = ColorTransformation.Palettes[Math.Min(so.ColorTranslation, (byte)(ColorTransformation.Palettes.Length - 1))];
            var rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                byte idx = src[i];
                uint c = pal[idx];
                rgba[i * 4] = (byte)(c >> 16); rgba[i * 4 + 1] = (byte)(c >> 8); rgba[i * 4 + 2] = (byte)c;
                rgba[i * 4 + 3] = idx == ColorTransformation.TRANSPARENTCOLORINDEX ? (byte)0 : (byte)255;
            }
            int shrink = (int)bgf.ShrinkFactor; if (shrink <= 0) shrink = 1;
            return (bmp, rgba, shrink);
        }
        catch (Exception ex) { GD.PrintErr("face part: " + ex.Message); return (null, null, 1); }
    }
}
