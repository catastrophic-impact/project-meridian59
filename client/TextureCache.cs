using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;

/// <summary>
/// Loads grdNNNNN.bgf grid bitmaps (frame 0) the way the original D3D client does for world geometry:
/// the stored bitmap is TRANSPOSED (texture x = stored row, texture y = stored column), and palette
/// index 254 is only transparent for "cutout" walls; otherwise it is an ordinary opaque colour.
/// </summary>
public class TextureCache
{
    public class Entry
    {
        public ImageTexture Texture;
        public int StoredWidth, StoredHeight;   // pDib->width / pDib->height in the original
        public int Shrink = 1;
        public bool HasAlpha;
    }

    readonly string dir;
    readonly Dictionary<long, Entry> cache = new Dictionary<long, Entry>();
    readonly Dictionary<int, BgfFile> files = new Dictionary<int, BgfFile>();

    public TextureCache(string resourceDir) { dir = resourceDir; }

    BgfFile File_(int gridId)
    {
        if (files.TryGetValue(gridId, out var f)) return f;
        string path = Path.Combine(dir, $"grd{gridId:D5}.bgf");
        try { f = File.Exists(path) ? new BgfFile(path) : null; }
        catch (System.Exception ex) { GD.PrintErr($"texture {gridId}: {ex.Message}"); f = null; }
        files[gridId] = f;
        return f;
    }

    /// <summary>Number of animation groups (frames) of a texture; 1 = not animated.</summary>
    public int Groups(int gridId) => System.Math.Max(1, File_(gridId)?.FrameSets.Count ?? 1);

    /// <param name="group">0-based animation group (the original shows group % numGroups)</param>
    public Entry Get(int gridId, bool cutout = false, int group = 0)
    {
        long key = ((long)gridId << 10) | ((long)(group & 0xFF) << 1) | (cutout ? 1L : 0L);
        if (cache.TryGetValue(key, out var e)) return e;
        e = Load(gridId, cutout, group);
        cache[key] = e;
        return e;
    }

    Entry Load(int gridId, bool cutout, int group)
    {
        try
        {
            var bgf = File_(gridId);
            if (bgf == null || bgf.Frames.Count == 0) return null;
            int fi = group <= 0 ? 0 : bgf.GetFrameIndex((group % System.Math.Max(1, bgf.FrameSets.Count)) + 1, 0);
            if (fi < 0) fi = 0;
            var bmp = bgf.Frames[fi];
            if (bmp.IsCompressed) bmp.IsCompressed = false;
            int w = (int)bmp.Width, h = (int)bmp.Height;
            var src = bmp.PixelData;
            if (w <= 0 || h <= 0 || src == null || src.Length < w * h) return null;

            uint[] pal = ColorTransformation.DefaultPalette;
            // transposed image: width = h, height = w; pixel(x=r, y=c) = src[row r][col c]
            var rgba = new byte[w * h * 4];
            bool alpha = false;
            byte lr = 128, lg = 128, lb = 128;   // original: cutout texels borrow the last opaque colour (no halo)
            for (int c = 0; c < w; c++)
            {
                for (int r = 0; r < h; r++)
                {
                    byte idx = src[r * w + c];
                    uint col = pal[idx];
                    int o = (c * h + r) * 4;
                    if (cutout && idx == ColorTransformation.TRANSPARENTCOLORINDEX)
                    {
                        rgba[o] = lr; rgba[o + 1] = lg; rgba[o + 2] = lb; rgba[o + 3] = 0; alpha = true;
                    }
                    else
                    {
                        lr = (byte)(col >> 16); lg = (byte)(col >> 8); lb = (byte)col;
                        rgba[o] = lr; rgba[o + 1] = lg; rgba[o + 2] = lb; rgba[o + 3] = 255;
                    }
                }
            }
            var img = Image.CreateFromData(h, w, false, Image.Format.Rgba8, rgba);
            img.GenerateMipmaps();
            int shrink = (int)bgf.ShrinkFactor; if (shrink <= 0) shrink = 1;
            return new Entry { Texture = ImageTexture.CreateFromImage(img), StoredWidth = w, StoredHeight = h, Shrink = shrink, HasAlpha = alpha };
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"texture {gridId}: {ex.Message}");
            return null;
        }
    }
}
