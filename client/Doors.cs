using System.Collections.Generic;
using Godot;
using Meridian59.Data.Models;

/// <summary>Doors and secret walls (BP_WALL_ANIMATE, official roomanim.c WallChange): the server changes the texture group of a wall by its server id, either at once
/// or as a one-shot / cycling animation, and may make it passable (open), impassable (closed) or invisible when the animation ends.  Animated walls are built as
/// their own mesh (see RoomBuilder.DynWalls) and rebuilt together with the lifts' mesh when a group changes.</summary>
public partial class Main
{
    class Door { public int Id; public bool Cycle; public double Period, Elapsed; public int Lo, Hi, Final, Group; public byte Action; }
    readonly List<Door> doors = new List<Door>();
    readonly HashSet<int> dynWalls = new HashSet<int>();
    readonly Dictionary<int, int> wallGroups = new Dictionary<int, int>();

    RoomBuilder MakeBuilder(Meridian59.Files.Room.RoomFile room) => new RoomBuilder(room, textures) { DynWalls = dynWalls, WallGroups = wallGroups };

    void ClearDoors() { doors.Clear(); dynWalls.Clear(); wallGroups.Clear(); }

    void OnWallAnimated(ushort id, Meridian59.Data.Models.Animation anim, byte action)
    {
        if (currentRoom == null || !roomShown) return;
        bool known = false;
        foreach (var sd in currentRoom.SideDefs) if (sd.ServerId == id) { known = true; break; }
        if (!known) return;
        bool grew = dynWalls.Add(id);
        doors.RemoveAll(d => d.Id == id);
        switch (anim)
        {
            case AnimationNone n:
                wallGroups[id] = n.Group;
                ApplyWallAction(id, action);
                break;
            case AnimationOnce o:
                doors.Add(new Door { Id = id, Cycle = false, Period = Mathf.Max(1, o.Period), Lo = o.GroupLow, Hi = o.GroupHigh, Final = o.GroupFinal, Group = o.GroupLow, Action = action });
                wallGroups[id] = o.GroupLow;
                break;
            case AnimationCycle c:
                doors.Add(new Door { Id = id, Cycle = true, Period = Mathf.Max(1, c.Period), Lo = c.GroupLow, Hi = c.GroupHigh, Group = c.GroupLow, Action = action });
                wallGroups[id] = c.GroupLow;
                break;
            default: return;
        }
        if (AnimLog) GD.Print($"[door] wall {id}: {anim.GetType().Name}, action {action}");
        if (grew) RebuildStaticRoom();
        dynDirty = true;
    }

    /// <summary>End action of a wall animation: 1 = passable, 2 = impassable, 3 = passable and no longer drawn.</summary>
    void ApplyWallAction(int id, byte action)
    {
        if (action == 0) return;
        foreach (var sd in currentRoom.SideDefs)
        {
            if (sd.ServerId != id) continue;
            if (action == 2) sd.Flags &= ~4u; else sd.Flags |= 4u;
            if (action == 3) sd.NormalBmp = 0;
        }
    }

    void UpdateDoors(float dt)
    {
        if (doors.Count == 0 || currentRoom == null) return;
        for (int i = doors.Count - 1; i >= 0; i--)
        {
            var d = doors[i];
            d.Elapsed += dt * 1000.0;
            int step = (int)(d.Elapsed / d.Period), span = Mathf.Max(1, d.Hi - d.Lo + 1), g;
            bool done = false;
            if (d.Cycle) g = d.Lo + step % span;
            else if (step >= span) { g = d.Final; done = true; }
            else g = d.Lo + step;
            if (g != d.Group) { d.Group = g; wallGroups[d.Id] = g; dynDirty = true; }
            if (done) { ApplyWallAction(d.Id, d.Action); doors.RemoveAt(i); dynDirty = true; }
        }
    }
}
