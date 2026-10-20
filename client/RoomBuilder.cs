using System;
using System.Collections.Generic;
using Godot;
using Meridian59.Files.Room;

/// <summary>Turns a RoomFile into a Godot Node3D with one mesh surface per texture.</summary>
public class RoomBuilder
{
    // world (as in the server and the official client): x east, y SOUTH (kod row grows southward; row < 1 = LEAVE_NORTH), z up, 1024 units per grid square.
    // Godot: (x, z(up), +y) so that -Z (Godot forward) is north. 1 Godot unit = 256 world units.
    public const float S = 1f / 256f;
    const int KodToWorld = 16;

    class Surf
    {
        public List<Vector3> V = new List<Vector3>();
        public List<Vector2> UV = new List<Vector2>();
        public List<Color> C = new List<Color>();
        public List<Vector3> N = new List<Vector3>();   // surface normal (Godot space), used for the sun's directional shading
        public List<int> I = new List<int>();
    }

    /// <summary>A material whose texture cycles through animation groups or scrolls (water, fires, torches ...). Driven by Main.UpdateRoomAnimations.</summary>
    public class RoomAnim
    {
        public ShaderMaterial Mat;
        public int Bmp; public bool Cutout;
        public bool Cycle;                 // true = group cycling, false = scrolling
        public float PeriodMs;             // cycle: ms per group; scroll: ms per step
        public int Groups;
        public int Dx, Dy;                 // scroll direction multipliers (official RoomAnimateSingle)
        public bool Wall, Backwards;
        public float TexW, TexH, Shrink;
        public int LastGroup = -1;
    }
    public readonly List<RoomAnim> Anims = new List<RoomAnim>();

    readonly RoomFile room;
    readonly TextureCache tex;
    readonly Dictionary<(long, int), Surf> surfs = new Dictionary<(long, int), Surf>();

    /// <summary>Walls the server animates (BP_WALL_ANIMATE: doors, secret walls): sidedef server ids built as their own mesh, and the texture group each currently shows.</summary>
    public HashSet<int> DynWalls; public Dictionary<int, int> WallGroups;
    bool IsDynWall(int sdNum) => DynWalls != null && sdNum > 0 && sdNum <= room.SideDefs.Count && room.SideDefs[sdNum - 1].ServerId != 0 && DynWalls.Contains(room.SideDefs[sdNum - 1].ServerId);
    int GroupOf(RoomSideDef sd) => WallGroups != null && sd.ServerId != 0 && WallGroups.TryGetValue(sd.ServerId, out var g) ? g : 0;

    // anim codes (part of the surface key so animated surfaces get their own material):
    //  0 none | (1<<16)|speed cycle | (2<<16)|(speed<<8)|(dir<<4)|backwards wall scroll | (3<<16)|(speed<<8)|dir floor/ceiling scroll
    int CycleCode(int bmp, byte speed) => speed != 0 && bmp > 0 && tex.Groups(bmp) > 1 ? (1 << 16) | speed : 0;
    static int WallScrollCode(uint flags, bool backwards) { int sp = (int)((flags & 0xC00) >> 10); return sp == 0 ? 0 : (2 << 16) | (sp << 8) | ((int)((flags & 0x7000) >> 12) << 4) | (backwards ? 1 : 0); }
    static int FlatScrollCode(uint flags, bool floor)
    {
        int sp = (int)((flags & 0xC) >> 2);
        if (sp == 0 || (flags & (floor ? 0x80u : 0x100u)) == 0) return 0;
        return (3 << 16) | (sp << 8) | (int)((flags & 0x70) >> 4);
    }

    public RoomBuilder(RoomFile room, TextureCache tex) { this.room = room; this.tex = tex; }

    public static Vector3 P(float x, float y, float z) => new Vector3(x * S, z * S, y * S);

    static long Key(int bmp, bool cutout, bool wall, bool noHTile = false, int group = 0) => ((long)bmp << 3) | (cutout ? 1L : 0L) | (wall ? 2L : 0L) | (noHTile ? 4L : 0L) | ((long)(group & 0xFF) << 40);

    Surf Get(int bmp, bool cutout, bool wall = false, bool noHTile = false, int anim = 0, int group = 0)
    {
        var k = (Key(bmp, cutout, wall, noHTile, group), anim);
        if (!surfs.TryGetValue(k, out var s)) surfs[k] = s = new Surf();
        return s;
    }

    /// <summary>Per-vertex light data for the room shader (see RoomLight): r = sector light, b = 1 if the sun shades this surface by its direction.</summary>
    static Color Shade(RoomSector sec, bool directional = false) => new Color(Mathf.Clamp(sec.Light / 255f, 0f, 1f), 0f, directional ? 1f : 0f);

    void Quad(int bmp, bool cutout, Vector3[] v, Vector2[] uv, Color c, Vector3 facing, bool noHTile = false, int anim = 0, int group = 0)
    {
        var s = Get(bmp, cutout, true, noHTile, anim, group);
        int b = s.V.Count;
        var nrm = facing.Normalized();
        for (int i = 0; i < 4; i++) { s.V.Add(v[i]); s.UV.Add(uv[i]); s.C.Add(c); s.N.Add(nrm); }
        // wall sides are one-sided (like the original's D3DCULL_CW). Pick the winding whose geometric normal points
        // toward the side that owns the sidedef (Godot: front face = clockwise).
        var n = (v[1] - v[0]).Cross(v[2] - v[0]);
        if (n.Dot(facing) < 0) s.I.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        else s.I.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
    }

    static float PlaneZ(float[] pl, float x, float y) => (-pl[0] * x - pl[1] * y - pl[3]) / pl[2];

    // lifts (BP_SECTOR_MOVE): sectors whose floor/ceiling can move are built separately so they can be rebuilt cheaply while they move.
    HashSet<int> dyn; bool dynPass;       // 1-based sector numbers; dynPass = build only the geometry touching them (otherwise only the rest)

    public Node3D Build(HashSet<int> dynamicSectors = null, bool dynamicPass = false)
    {
        dyn = dynamicSectors; dynPass = dynamicPass;
        var root = new Node3D { Name = "Room" };
        BuildFlats();
        BuildWalls();

        var mesh = new ArrayMesh();
        var mats = new List<Material>();
        foreach (var kv in surfs)
        {
            var s = kv.Value;
            var arr = new Godot.Collections.Array();
            arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = s.V.ToArray();
            arr[(int)Mesh.ArrayType.TexUV] = s.UV.ToArray();
            arr[(int)Mesh.ArrayType.Color] = s.C.ToArray();
            arr[(int)Mesh.ArrayType.Normal] = s.N.ToArray();
            arr[(int)Mesh.ArrayType.Index] = s.I.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            long surfKey = kv.Key.Item1; int animCode = kv.Key.Item2;
            int bmpId = (int)((surfKey >> 3) & 0xFFFFFFFFL); int grp = (int)((surfKey >> 40) & 0xFF); bool cut = (surfKey & 1) != 0; bool isWall = (surfKey & 2) != 0; bool noH = (surfKey & 4) != 0;
            var e = bmpId > 0 ? tex.Get(bmpId, cut, grp) : null;
            bool repeat = !noH || (animCode >> 16) >= 2;
            var m = new ShaderMaterial { Shader = RoomLight.GetShader(isWall, repeat) };
            if (e != null) { m.SetShaderParameter("tex", e.Texture); m.SetShaderParameter("cutout", e.HasAlpha); }
            else { m.SetShaderParameter("tex", RoomLight.Purple); m.SetShaderParameter("cutout", false); }   // missing texture: purple
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
            if (animCode != 0 && e != null) RegisterAnim(m, animCode, bmpId, cut, isWall, e);
        }
        root.AddChild(new MeshInstance3D { Mesh = mesh, Name = "Geometry" });
        return root;
    }

    void RegisterAnim(ShaderMaterial m, int code, int bmpId, bool cut, bool wall, TextureCache.Entry e)
    {
        int type = code >> 16;
        var a = new RoomAnim { Mat = m, Bmp = bmpId, Cutout = cut, Wall = wall, TexW = e.StoredWidth, TexH = e.StoredHeight, Shrink = e.Shrink };
        if (type == 1) { a.Cycle = true; a.PeriodMs = 10000f / (code & 0xFF); a.Groups = tex.Groups(bmpId); }
        else
        {
            int speed = (code >> 8) & 3, dir = type == 2 ? (code >> 4) & 7 : code & 7;
            a.Backwards = type == 2 && (code & 1) != 0;
            a.PeriodMs = type == 2 ? (speed == 1 ? 96f : speed == 2 ? 32f : 8f) : (speed == 1 ? 12f : speed == 2 ? 6f : 2f);   // official roomanim.h
            switch (dir)   // official RoomAnimateSingle: offsets accumulate (dx, dy) per step
            {
                case 0: a.Dx = 0; a.Dy = -1; break;  case 1: a.Dx = -1; a.Dy = -1; break;
                case 2: a.Dx = -1; a.Dy = 0; break;  case 3: a.Dx = -1; a.Dy = 1; break;
                case 4: a.Dx = 0; a.Dy = 1; break;   case 5: a.Dx = 1; a.Dy = 1; break;
                case 6: a.Dx = 1; a.Dy = 0; break;   default: a.Dx = 1; a.Dy = -1; break;
            }
        }
        Anims.Add(a);
    }

    void BuildFlats()
    {
        foreach (var n in room.Nodes)
        {
            if (!n.IsLeaf || n.Poly.Count < 3 || n.SectorNum == 0 || n.SectorNum > room.Sectors.Count) continue;
            var sec = room.Sectors[n.SectorNum - 1];
            if (dyn != null && dyn.Contains(n.SectorNum) != dynPass) continue;
            if (sec.FloorBmp != 0) Flat(n, sec, sec.FloorBmp, sec.FloorHeight * KodToWorld, sec.FloorPlane, true);
            if (sec.CeilingBmp != 0) Flat(n, sec, sec.CeilingBmp, sec.CeilingHeight * KodToWorld, sec.CeilingPlane, false);   // none = open sky
        }
    }

    void Flat(RoomNode n, RoomSector sec, int bmp, float z, float[] plane, bool floor)
    {
        int anim = CycleCode(bmp, sec.Speed);
        if (anim == 0 && sec.Speed == 0) anim = FlatScrollCode(sec.Flags, floor);
        var s = Get(bmp, false, false, false, anim);
        // sloped floors/ceilings that are steep enough are shaded by the sun's direction (official SLF_DIRECTIONAL, |c| < 1010/1024)
        var nrm = Vector3.Up; bool dirl = false;
        if (plane != null && Math.Abs(plane[2]) > 1e-6f)
        {
            float ln = MathF.Sqrt(plane[0] * plane[0] + plane[1] * plane[1] + plane[2] * plane[2]);
            if (ln > 1e-6f && Math.Abs(plane[2]) / ln < 1010f / 1024f) { dirl = true; nrm = new Vector3(plane[0], plane[2], plane[1]) / ln; }
        }
        var col = Shade(sec, dirl);
        int b = s.V.Count;
        foreach (var (x, y) in n.Poly)
        {
            float zz = (plane != null && Math.Abs(plane[2]) > 1e-6f) ? PlaneZ(plane, x, y) : z;
            s.V.Add(P(x, y, zz));
            // original: s = (y - ty)/1024, t = (x - tx)/1024 with ty,tx in client units (kod << 4)
            s.UV.Add(new Vector2((y - sec.TexY * 16f) / 1024f, (x - sec.TexX * 16f) / 1024f));
            s.C.Add(col); s.N.Add(nrm);
        }
        for (int i = 1; i + 1 < n.Poly.Count; i++) s.I.AddRange(new[] { b, b + i, b + i + 1 });
    }

    const uint WF_BACKWARDS = 0x1, WF_TRANSPARENT = 0x2, WF_ABOVE_BOTTOMUP = 0x40, WF_BELOW_TOPDOWN = 0x80,
               WF_NORMAL_TOPDOWN = 0x100, WF_NO_VTILE = 0x200, WF_NO_HTILE = 0x20000;
    enum WallPart { Normal, Below, Above }

    void BuildWalls()
    {
        foreach (var w in room.Walls)
        {
            if (dyn != null && (dyn.Contains(w.PosSector) || dyn.Contains(w.NegSector) || IsDynWall(w.PosSideDef) || IsDynWall(w.NegSideDef)) != dynPass) continue;
            Side(w, w.PosSideDef, w.PosSector, w.NegSector, w.PosXOff, w.PosYOff, true, w.NegSideDef);
            Side(w, w.NegSideDef, w.NegSector, w.PosSector, w.NegXOff, w.NegYOff, false, w.PosSideDef);
        }
    }

    float SecFloor(RoomSector s, float x, float y) => (s.FloorPlane != null && Math.Abs(s.FloorPlane[2]) > 1e-6f) ? PlaneZ(s.FloorPlane, x, y) : s.FloorHeight * KodToWorld;
    float SecCeil(RoomSector s, float x, float y) => (s.CeilingPlane != null && Math.Abs(s.CeilingPlane[2]) > 1e-6f) ? PlaneZ(s.CeilingPlane, x, y) : s.CeilingHeight * KodToWorld;

    // Heights are evaluated at BOTH wall endpoints (sloped floors/ceilings), like the original's SetWallHeights: index 0 = (X0,Y0), 1 = (X1,Y1).
    void Side(RoomWall w, int sdNum, int secNum, int otherNum, int xoff, int yoff, bool pos, int otherSdNum)
    {
        if (sdNum == 0 || sdNum > room.SideDefs.Count || secNum == 0 || secNum > room.Sectors.Count) return;
        var sd = room.SideDefs[sdNum - 1];
        var osd = (otherSdNum > 0 && otherSdNum <= room.SideDefs.Count) ? room.SideDefs[otherSdNum - 1] : null;
        var sec = room.Sectors[secNum - 1];
        float zf0 = SecFloor(sec, w.X0, w.Y0), zf1 = SecFloor(sec, w.X1, w.Y1), zc0 = SecCeil(sec, w.X0, w.Y0), zc1 = SecCeil(sec, w.X1, w.Y1);
        if (otherNum == 0 || otherNum > room.Sectors.Count)
        {
            WallQuad(w, sd, osd, sd.NormalBmp, WallPart.Normal, zf0, zc0, zf1, zc1, xoff, yoff, pos, sec);
            return;
        }
        var oth = room.Sectors[otherNum - 1];
        float of0 = SecFloor(oth, w.X0, w.Y0), of1 = SecFloor(oth, w.X1, w.Y1), oc0 = SecCeil(oth, w.X0, w.Y0), oc1 = SecCeil(oth, w.X1, w.Y1);
        if (of0 > zf0 || of1 > zf1) WallQuad(w, sd, osd, sd.BelowBmp, WallPart.Below, zf0, Math.Max(of0, zf0), zf1, Math.Max(of1, zf1), xoff, yoff, pos, sec);
        if (oc0 < zc0 || oc1 < zc1) WallQuad(w, sd, osd, sd.AboveBmp, WallPart.Above, Math.Min(oc0, zc0), zc0, Math.Min(oc1, zc1), zc1, xoff, yoff, pos, sec);
        if (sd.NormalBmp != 0)
        {
            float lo0 = Math.Max(zf0, of0), hi0 = Math.Min(zc0, oc0), lo1 = Math.Max(zf1, of1), hi1 = Math.Min(zc1, oc1);
            if (hi0 > lo0 || hi1 > lo1) WallQuad(w, sd, osd, sd.NormalBmp, WallPart.Normal, lo0, Math.Max(hi0, lo0), lo1, Math.Max(hi1, lo1), xoff, yoff, pos, sec);
        }
    }

    // Port of D3DRenderWallExtract (clientd3d/d3drender_world.c), including sloped (trapezoid) walls: texture t is computed per vertex from the heights.
    // b0/t0 = bottom/top at endpoint 0 (X0,Y0), b1/t1 at endpoint 1.
    void WallQuad(RoomWall w, RoomSideDef sd, RoomSideDef osd, int bmp, WallPart part, float b0, float t0, float b1, float t1,
                  int xoff, int yoff, bool pos, RoomSector sec)
    {
        if (bmp == 0 || (t0 <= b0 && t1 <= b1)) return;   // no bitmap = nothing drawn
        // cutout (index 254 transparent) only if this side, or the other side showing the same bitmap, is transparent
        bool cutout = (sd.Flags & WF_TRANSPARENT) != 0 ||
                      (osd != null && (osd.Flags & WF_TRANSPARENT) != 0 &&
                       (osd.NormalBmp == bmp || osd.AboveBmp == bmp || osd.BelowBmp == bmp));
        var e = bmp > 0 ? tex.Get(bmp, cutout) : null;
        float W = e?.StoredWidth ?? 64, H = e?.StoredHeight ?? 64, shrink = e?.Shrink ?? 1;
        float invW = 1f / W, invH = 1f / H, invWFudge = 1f / (W * 16f), k = shrink * invWFudge;

        bool topDown = part switch
        {
            WallPart.Normal => (sd.Flags & WF_NORMAL_TOPDOWN) != 0,
            WallPart.Below => (sd.Flags & WF_BELOW_TOPDOWN) != 0,
            _ => (sd.Flags & WF_ABOVE_BOTTOMUP) == 0,
        };

        // official vertex order: 0 = start/top, 1 = start/bottom, 2 = end/bottom, 3 = end/top; pos side runs p0 -> p1, neg side p1 -> p0
        float z0, z1, z2, z3;
        if (pos) { z0 = t0; z1 = b0; z2 = b1; z3 = t1; } else { z0 = t1; z1 = b1; z2 = b0; z3 = t0; }

        short yo = (short)yoff;
        float s0 = xoff * shrink * invH;
        float s3 = s0 + w.Length * shrink * invH;
        float tv0, tv1, tv2, tv3;
        bool flat = z0 == z3 && z1 == z2;
        float top, bottom;
        if (!topDown)
        {
            bottom = z1 == z2 ? z1 : (float)Math.Floor(Math.Min(z1, z2) / 1024f) * 1024f;
            tv1 = tv2 = 1f - yo * shrink * invW;
            if (z1 != z2) { tv1 -= (z1 - bottom) * k; tv2 -= (z2 - bottom) * k; }
            tv0 = tv1 - (z0 - z1) * k;
            tv3 = tv2 - (z3 - z2) * k;
        }
        else
        {
            top = z0 == z3 ? z0 : (float)Math.Ceiling(Math.Max(z0, z3) / 1024f) * 1024f;
            tv0 = tv3 = 0f;
            if (z0 != z3) { tv0 = (top - z0) * k; tv3 = (top - z3) * k; }
            tv0 -= yo * shrink * invW; tv3 -= yo * shrink * invW;
            tv1 = tv0 + (z0 - z1) * k;
            tv2 = tv3 + (z3 - z2) * k;
        }
        float sA = s0, sB = s3;
        if ((sd.Flags & WF_BACKWARDS) != 0) { sA = s3; sB = s0; }
        // WF_NO_VTILE (cutout normal walls): the texture is not repeated upwards, so the wall ends where the texture does (flat walls only)
        bool noV = flat && part == WallPart.Normal && (sd.Flags & WF_NO_VTILE) != 0 && cutout;
        if (noV)
        {
            if (tv0 < 0f)
            {
                float tex = tv1 - tv0; if (tex == 0f) tex = 1f;
                z0 -= (z0 - z1) * (-tv0 / tex); z3 = z0;
                tv0 = tv3 = 0f;
            }
            z1 -= 16f; z2 = z1;
        }
        tv0 += invW; tv3 += invW; tv1 -= invW; tv2 -= invW;

        float ax, ay, bx, by;
        if (pos) { ax = w.X0; ay = w.Y0; bx = w.X1; by = w.Y1; } else { ax = w.X1; ay = w.Y1; bx = w.X0; by = w.Y0; }
        // pos side normal is (-dy, dx) in world xy; world (nx,ny) -> Godot (nx, 0, ny)
        float dx = w.X1 - w.X0, dy = w.Y1 - w.Y0;
        var facing = pos ? new Vector3(-dy, 0, dx) : new Vector3(dy, 0, -dx);
        int anim = CycleCode(bmp, sd.Speed);
        if (anim == 0 && sd.Speed == 0) anim = WallScrollCode(sd.Flags, (sd.Flags & WF_BACKWARDS) != 0);
        Quad(bmp, cutout,
             new[] { P(ax, ay, z0), P(ax, ay, z1), P(bx, by, z2), P(bx, by, z3) },
             new[] { new Vector2(sA, tv0), new Vector2(sA, tv1), new Vector2(sB, tv2), new Vector2(sB, tv3) },
             Shade(sec, true), facing, part == WallPart.Normal && (sd.Flags & WF_NO_HTILE) != 0 && cutout, anim, GroupOf(sd));
    }

    /// <summary>A reasonable place to start looking at the room.</summary>
    public Vector3 FindStart(out float yaw)
    {
        yaw = 0;
        float cx = room.Width / 2f, cy = room.Height / 2f;
        int sn = room.FindSector(cx, cy);
        if (sn == 0)
        {
            foreach (var n in room.Nodes)
                if (n.IsLeaf && n.Poly.Count >= 3 && n.SectorNum != 0)
                {
                    float x = 0, y = 0; foreach (var (px, py) in n.Poly) { x += px; y += py; }
                    cx = x / n.Poly.Count; cy = y / n.Poly.Count; sn = n.SectorNum; break;
                }
        }
        float floor = sn > 0 ? room.Sectors[sn - 1].FloorHeight * KodToWorld : 0;
        return P(cx, cy, floor + 600);
    }
}
