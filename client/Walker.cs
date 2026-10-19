using System;
using System.Collections.Generic;
using Meridian59.Files.Room;

/// <summary>
/// First-person walking with wall collision, following the rules of the original client (clientd3d/move.c):
/// stay 48 units from walls, slide along them, walls block unless flagged passable and the step/headroom is OK.
/// All units are client "fineness" (1024 per grid square); X east, Y north, Z up.
/// </summary>
public class Walker
{
    public const float Radius = 48f;                 // min_distance
    public const float MaxStep = 24 * 16f;           // MAX_STEP_HEIGHT = 24 kod units
    public const float EyeHeight = 768f;             // player.height = 3/4 grid square
    public const float WalkSpeed = 18f * 102.4f;     // server speed 18 = 1.8 grid squares per second
    const uint WF_PASSABLE = 0x4;

    readonly RoomFile room;
    public float X, Y, Z;                            // Z = feet (floor) height

    public Walker(RoomFile room) { this.room = room; }

    public float FloorAt(float x, float y)
    {
        int sn = room.FindSector(x, y);
        if (sn == 0) return float.NaN;
        var s = room.Sectors[sn - 1];
        if (s.FloorPlane != null && Math.Abs(s.FloorPlane[2]) > 1e-6f)
            return (-s.FloorPlane[0] * x - s.FloorPlane[1] * y - s.FloorPlane[3]) / s.FloorPlane[2];
        return s.FloorHeight * 16f;
    }

    /// <summary>Wading depth of a sector's flags (low two bits): official sector_depths = FINENESS/5 * {0,1,2,3}.</summary>
    public static readonly float[] Depths = { 0f, 204f, 409f, 614f };
    public static float DepthOf(uint sectorFlags) => Depths[sectorFlags & 3];

    public float DepthAt(float x, float y)
    {
        int sn = room.FindSector(x, y);
        return sn == 0 ? 0f : DepthOf(room.Sectors[sn - 1].Flags);
    }

    /// <summary>Official GetFloorBase: the floor minus the wading depth. That is where the feet (and so the eyes) are.</summary>
    public float FloorBaseAt(float x, float y)
    {
        float f = FloorAt(x, y);
        return float.IsNaN(f) ? f : f - DepthAt(x, y);
    }

    /// <summary>Movement slowdown while wading (official move.c): 3/4, 1/2, 1/4.</summary>
    public float WadeFactor()
    {
        int sn = room.FindSector(X, Y);
        return sn == 0 ? 1f : (room.Sectors[sn - 1].Flags & 3) switch { 1 => 0.75f, 2 => 0.5f, 3 => 0.25f, _ => 1f };
    }

    public bool Place(float x, float y)
    {
        float f = FloorBaseAt(x, y);
        if (float.IsNaN(f)) return false;
        X = x; Y = y; Z = f;
        return true;
    }

    /// <summary>Move by (dx,dy) in sub-steps, sliding along blocking walls. Returns true if the position changed.</summary>
    public bool Move(float dx, float dy)
    {
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return false;
        int n = (int)MathF.Ceiling(len / 24f);
        float sx = X, sy = Y;
        for (int i = 0; i < n; i++) Step(dx / n, dy / n);
        return Math.Abs(X - sx) + Math.Abs(Y - sy) > 0.01f;
    }

    void Step(float dx, float dy)
    {
        float ox = X, oy = Y;
        float nx = ox + dx, ny = oy + dy;

        // push out of blocking walls (this is what makes the player slide along them)
        for (int iter = 0; iter < 3; iter++)
        {
            bool pushed = false;
            foreach (var w in room.Walls)
            {
                float minx = Math.Min(w.X0, w.X1) - Radius, maxx = Math.Max(w.X0, w.X1) + Radius;
                float miny = Math.Min(w.Y0, w.Y1) - Radius, maxy = Math.Max(w.Y0, w.Y1) + Radius;
                if (nx < minx || nx > maxx || ny < miny || ny > maxy) continue;

                float wx = w.X1 - w.X0, wy = w.Y1 - w.Y0;
                float l2 = wx * wx + wy * wy;
                if (l2 < 1e-6f) continue;
                float t = Math.Clamp(((nx - w.X0) * wx + (ny - w.Y0) * wy) / l2, 0f, 1f);
                float cx = w.X0 + wx * t, cy = w.Y0 + wy * t;
                float ex = nx - cx, ey = ny - cy;
                float d2 = ex * ex + ey * ey;
                if (d2 >= Radius * Radius) continue;

                // which side of the wall we were on before this step (positive side = left of p0->p1)
                bool onPos = (wx * (oy - w.Y0) - wy * (ox - w.X0)) > 0.001f;
                if (!Blocks(w, onPos)) continue;

                float d = MathF.Sqrt(d2);
                float px, py;
                if (d > 1e-3f) { px = ex / d; py = ey / d; }
                else
                {   // exactly on the wall: push toward the side we came from
                    float nl = MathF.Sqrt(l2);
                    px = -wy / nl; py = wx / nl;
                    if (!onPos) { px = -px; py = -py; }
                }
                // don't let the push drag us to the far side of a wall we were not on
                nx = cx + px * Radius; ny = cy + py * Radius;
                pushed = true;
            }
            if (!pushed) break;
        }

        if (room.FindSector(nx, ny) == 0) return;       // never leave the room's floor area
        float nz = FloorBaseAt(nx, ny);
        if (!float.IsNaN(nz) && nz - Z > MaxStep + 1f) return;   // too high to step up
        X = nx; Y = ny;
    }

    bool Blocks(RoomWall w, bool onPos)
    {
        int sdn = onPos ? w.PosSideDef : w.NegSideDef;
        if (sdn == 0 || sdn > room.SideDefs.Count) return false;       // no sidedef on our side: not an obstacle
        var sd = room.SideDefs[sdn - 1];
        if ((sd.Flags & WF_PASSABLE) == 0) return true;

        int mine = onPos ? w.PosSector : w.NegSector, other = onPos ? w.NegSector : w.PosSector;
        if (mine == 0 || other == 0 || mine > room.Sectors.Count || other > room.Sectors.Count) return false;
        var a = room.Sectors[mine - 1]; var b = room.Sectors[other - 1];
        float z1 = Math.Max(a.FloorHeight, b.FloorHeight) * 16f;        // higher of the two floors
        float z2 = Math.Min(a.CeilingHeight, b.CeilingHeight) * 16f;    // lower of the two ceilings
        bool belowOk = sd.BelowBmp == 0 || (z1 - DepthOf(b.Flags) - Z) <= MaxStep;   // wading on the far side lowers the effective step (official move.c)
        bool aboveOk = sd.AboveBmp == 0 || (z2 - Z) >= EyeHeight;
        return !(belowOk && aboveOk);
    }

    /// <summary>Ease the feet height toward the floor under us (climb steps / fall).</summary>
    public void UpdateZ(float dt)
    {
        float f = FloorBaseAt(X, Y);
        if (float.IsNaN(f)) return;
        if (Z < f) Z = Math.Min(f, Z + 2500f * dt);          // climb
        else if (Z > f) Z = Math.Max(f, Z - 5000f * dt);     // fall
    }
}
