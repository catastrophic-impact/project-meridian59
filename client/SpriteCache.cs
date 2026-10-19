using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;

/// <summary>Loads object bitmaps (.bgf) as upright, cutout textures for billboards (objects use the non-transposed layout).</summary>
public class SpriteCache
{
    public class Entry
    {
        public ImageTexture Texture;
        public int Width, Height, Shrink = 1;
        public int XOffset, YOffset;
    }

    readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
    readonly Dictionary<string, BgfFile> files = new Dictionary<string, BgfFile>();

    BgfFile File(string path)
    {
        if (!files.TryGetValue(path, out var f))
        {
            try { f = new BgfFile(path); } catch (System.Exception ex) { GD.PrintErr($"sprite {path}: {ex.Message}"); f = null; }
            files[path] = f;
        }
        return f;
    }

    /// <summary>Number of animation groups in a file (0 if unreadable).</summary>
    public int Groups(string path) => File(path)?.FrameSets.Count ?? 0;

    /// <summary>The bitmap for an (1-based) animation group seen from a relative view angle (0..4095, 0 = front); the frame set of the group decides how many angles exist.</summary>
    public Entry Get(string path, int group = 1, ushort angle = 0, byte translation = 0)
    {
        var bgf = File(path);
        if (bgf == null || bgf.Frames.Count == 0) return null;
        int idx = bgf.GetFrameIndex(group < 1 ? 1 : group, angle);
        if (idx < 0) idx = 0;
        string key = translation == 0 ? path + "#" + idx : path + "#" + idx + "x" + translation;
        if (cache.TryGetValue(key, out var e)) return e;
        e = Load(bgf, idx, translation);
        cache[key] = e;
        return e;
    }

    Entry Load(BgfFile bgf, int frameIndex, byte translation = 0)
    {
        string path = "";
        try
        {
            var bmp = bgf.Frames[frameIndex];
            if (bmp.IsCompressed) bmp.IsCompressed = false;
            int w = (int)bmp.Width, h = (int)bmp.Height;
            var src = bmp.PixelData;
            if (w <= 0 || h <= 0 || src == null || src.Length < w * h) return null;

            uint[] pal = translation == 0 ? ColorTransformation.DefaultPalette : ColorTransformation.Palettes[Math.Min((int)translation, ColorTransformation.Palettes.Length - 1)];
            var rgba = new byte[w * h * 4];
            byte lr = 128, lg = 128, lb = 128;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;                         // sprite rows are stored top-down (verified: bottom-up made them upside down)
                for (int x = 0; x < w; x++)
                {
                    byte idx = src[row + x];
                    uint c = pal[idx];
                    int o = (y * w + x) * 4;
                    if (idx == ColorTransformation.TRANSPARENTCOLORINDEX)
                    { rgba[o] = lr; rgba[o + 1] = lg; rgba[o + 2] = lb; rgba[o + 3] = 0; }
                    else
                    {
                        lr = (byte)(c >> 16); lg = (byte)(c >> 8); lb = (byte)c;
                        rgba[o] = lr; rgba[o + 1] = lg; rgba[o + 2] = lb; rgba[o + 3] = 255;
                    }
                }
            }
            var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
            int shrink = (int)bgf.ShrinkFactor; if (shrink <= 0) shrink = 1;
            return new Entry { Texture = ImageTexture.CreateFromImage(img), Width = w, Height = h, Shrink = shrink, XOffset = bmp.XOffset, YOffset = bmp.YOffset };
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"sprite {path}: {ex.Message}");
            return null;
        }
    }
}
