// Safe, managed .roo reader. Follows the official client loader (clientd3d/bspload.c).
// Supports versions >= 10; v13+ stores coordinates as floats. Grids are ignored.
using System;
using System.Collections.Generic;
using System.IO;

namespace Meridian59.Files.Room
{
    public class RoomNode
    {
        public bool IsLeaf;
        public float X0, Y0, X1, Y1;                 // bounding box
        // internal node: separator line a*x + b*y + c = 0
        public float A, B, C;
        public ushort PosNum, NegNum, WallNum;       // 1-based indices, 0 = none
        // leaf
        public ushort SectorNum;                     // 1-based? (as stored; 0 = none)
        public List<(float X, float Y)> Poly = new List<(float, float)>();
    }

    public class RoomWall
    {
        public ushort NextNum, PosSideDef, NegSideDef;
        public float X0, Y0, X1, Y1, Length;
        public short PosXOff, NegXOff, PosYOff, NegYOff;
        public ushort PosSector, NegSector;
    }

    public class RoomSideDef
    {
        public ushort ServerId, NormalBmp, AboveBmp, BelowBmp;
        public uint Flags;
        public byte Speed;
    }

    public class RoomSector
    {
        public ushort ServerId, FloorBmp, CeilingBmp;
        public ushort TexX, TexY;                    // kod units
        public ushort FloorHeight, CeilingHeight;    // kod units (1/64 grid); client units = <<4
        public byte Light;
        public uint Flags;
        public byte Speed;
        public float[] FloorPlane, CeilingPlane;     // a,b,c,d when sloped
    }

    public class RoomFile
    {
        public const uint Magic = 0xB14F4F52;        // bytes 52 4F 4F B1 ("ROO\xB1")
        public const uint SF_SLOPED_FLOOR = 0x400, SF_SLOPED_CEILING = 0x800;

        public int Version;
        public int Security;
        public bool SecurityOk;
        public uint Width, Height;                   // client fineness units (1024 per grid square)
        public List<RoomNode> Nodes = new List<RoomNode>();
        public List<RoomWall> Walls = new List<RoomWall>();
        public List<RoomSideDef> SideDefs = new List<RoomSideDef>();
        public List<RoomSector> Sectors = new List<RoomSector>();

        byte[] d; int p; int sec;

        public static RoomFile Load(string path) => Parse(File.ReadAllBytes(path));

        public static RoomFile Parse(byte[] data)
        {
            var r = new RoomFile { d = data };
            r.Read();
            r.d = null;
            return r;
        }

        // ---- primitive readers (bounds checked via exceptions) ----
        byte U8() => d[p++];
        ushort U16() { ushort v = BitConverter.ToUInt16(d, p); p += 2; return v; }
        short S16() { short v = BitConverter.ToInt16(d, p); p += 2; return v; }
        uint U32() { uint v = BitConverter.ToUInt32(d, p); p += 4; return v; }
        int S32() { int v = BitConverter.ToInt32(d, p); p += 4; return v; }

        // coordinate: int in <v13, float in >=v13. Adds raw bits to security sum when requested.
        float Val(bool addSec)
        {
            int raw = S32();
            if (addSec) sec = unchecked(sec + raw);
            return Version < 13 ? (float)raw : BitConverter.Int32BitsToSingle(raw);
        }

        void Read()
        {
            if (d.Length < 16 || U32() != Magic) throw new InvalidDataException("not a roo file");
            Version = S32();
            if (Version < 10) throw new InvalidDataException("roo version too old: " + Version);
            sec = Version;
            Security = S32();
            int mainInfo = S32();
            p = mainInfo;
            Width = U32(); Height = U32();
            int nodePos = S32(), wallPos = S32(); S32(); int sidePos = S32(), sectorPos = S32();

            p = nodePos; int nn = U16();
            for (int i = 0; i < nn; i++) Nodes.Add(ReadNode());
            p = wallPos; int nw = U16();
            for (int i = 0; i < nw; i++) Walls.Add(ReadWall());
            p = sidePos; int ns = U16();
            for (int i = 0; i < ns; i++) SideDefs.Add(ReadSideDef());
            p = sectorPos; int nsec = U16();
            for (int i = 0; i < nsec; i++) Sectors.Add(ReadSector());

            SecurityOk = unchecked(sec ^ (int)0x89ab786c) == Security;
            NormalizeSeparators();
        }

        // Same as the official RoomSwizzle: normalise (a,b) and recompute c from the splitting wall.
        void NormalizeSeparators()
        {
            foreach (var n in Nodes)
            {
                if (n.IsLeaf || n.WallNum == 0 || n.WallNum > Walls.Count) continue;
                var w = Walls[n.WallNum - 1];
                double len = Math.Sqrt((double)n.A * n.A + (double)n.B * n.B);
                if (len <= 0) continue;
                double a = n.A / len, b = n.B / len;
                n.A = (float)a; n.B = (float)b;
                n.C = (float)(-((a * w.X1 + b * w.Y1) + (a * w.X0 + b * w.Y0)) / 2.0);
            }
        }

        /// <summary>Descend the BSP tree; returns the 1-based sector number at (x,y), or 0 if none.</summary>
        public int FindSector(float x, float y)
        {
            if (Nodes.Count == 0) return 0;
            int idx = 0;
            for (int guard = 0; guard < 10000; guard++)
            {
                var n = Nodes[idx];
                if (n.IsLeaf) return n.SectorNum;
                double side = (double)n.A * x + (double)n.B * y + n.C;
                int next = side >= 0 ? n.PosNum : n.NegNum;
                if (next == 0) return 0;
                idx = next - 1;
            }
            return 0;
        }

        RoomNode ReadNode()
        {
            var n = new RoomNode();
            byte type = U8();
            n.X0 = Val(false); n.Y0 = Val(false); n.X1 = Val(false); n.Y1 = Val(false);
            if (type == 1)
            {
                n.A = Val(true); n.B = Val(true); n.C = Val(true);
                n.PosNum = U16(); n.NegNum = U16(); n.WallNum = U16();
                sec = unchecked(sec + n.WallNum);
            }
            else if (type == 2)
            {
                n.IsLeaf = true;
                n.SectorNum = U16();
                int np = U16();
                for (int i = 0; i < np; i++) { float x = Val(true); float y = Val(true); n.Poly.Add((x, y)); }
            }
            else throw new InvalidDataException("unknown BSP node type " + type);
            return n;
        }

        RoomWall ReadWall()
        {
            var w = new RoomWall();
            w.NextNum = U16(); w.PosSideDef = U16(); w.NegSideDef = U16();
            sec = unchecked(sec + w.PosSideDef + w.NegSideDef);
            w.X0 = Val(true); w.Y0 = Val(true); w.X1 = Val(true); w.Y1 = Val(true);
            w.Length = Version < 13 ? U16() : BitConverter.Int32BitsToSingle(S32());
            w.PosXOff = S16(); w.NegXOff = S16(); w.PosYOff = S16(); w.NegYOff = S16();
            w.PosSector = U16(); w.NegSector = U16();
            sec = unchecked(sec + w.PosSector + w.NegSector);
            return w;
        }

        RoomSideDef ReadSideDef()
        {
            var s = new RoomSideDef();
            s.ServerId = U16(); sec = unchecked(sec + s.ServerId);
            s.NormalBmp = U16(); s.AboveBmp = U16(); s.BelowBmp = U16();
            sec = unchecked(sec + s.AboveBmp + s.BelowBmp + s.NormalBmp);
            s.Flags = U32(); sec = unchecked(sec + (int)s.Flags);
            s.Speed = U8();
            return s;
        }

        RoomSector ReadSector()
        {
            var s = new RoomSector();
            s.ServerId = U16(); sec = unchecked(sec + s.ServerId);
            s.FloorBmp = U16(); s.CeilingBmp = U16();
            sec = unchecked(sec + s.FloorBmp + s.CeilingBmp);
            s.TexX = U16(); s.TexY = U16();
            s.FloorHeight = U16(); sec = unchecked(sec + s.FloorHeight);
            s.CeilingHeight = U16(); sec = unchecked(sec + s.CeilingHeight);
            s.Light = U8(); sec = unchecked(sec + s.Light);
            s.Flags = U32(); sec = unchecked(sec + (int)s.Flags);
            s.Speed = U8();
            if ((s.Flags & SF_SLOPED_FLOOR) != 0) s.FloorPlane = ReadSlope();
            if ((s.Flags & SF_SLOPED_CEILING) != 0) s.CeilingPlane = ReadSlope();
            return s;
        }

        float[] ReadSlope()
        {
            var pl = new float[4];
            for (int i = 0; i < 4; i++) pl[i] = Val(false);
            Val(false); Val(false);     // texture origin x,y
            p += 4;                     // texture angle
            p += 18;                    // unused
            return pl;
        }
    }
}
