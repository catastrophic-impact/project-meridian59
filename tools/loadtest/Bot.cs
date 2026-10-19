using System;
using System.IO;
using Meridian59.Files.Room;

namespace Bots;

/// <summary>One headless player: a GameClient plus a tiny wander/chat behaviour. Call Tick() ~20x per second.</summary>
public class Bot
{
    public readonly GameClient Client = new GameClient();
    public readonly string Name;
    readonly string roomDir;
    readonly Random rng;
    readonly DateTime t0 = DateTime.UtcNow;

    RoomFile room;
    Walker walker;
    DateTime lastTick = DateTime.UtcNow, lastSend = DateTime.MinValue, enteredAt = DateTime.MaxValue;
    DateTime idleUntil = DateTime.MinValue, nextChat = DateTime.MaxValue;
    float targetX, targetY, lastSentX, lastSentY;
    bool walking, unsentMove;
    ushort lastAngle = 0xFFFF;
    DateTime lastTurn = DateTime.MinValue;

    public int MovesSent, ChatsSent, RoomsEntered;
    public bool Ready => Client.State == GameClient.Phase.Playing && walker != null;

    static readonly string[] Lines = { "Hello there.", "Fine day for adventure.", "Anyone seen any ants around?", "Careful out here.", "Well met.", "*sigh*" };

    public Bot(string name, string rsbPath, string roomDir, int seed)
    {
        Name = name; this.roomDir = roomDir; rng = new Random(seed);
        Client.LoadStrings(rsbPath);
        Client.Log += s => { if (Verbose) Console.WriteLine($"[{Name}] {s}"); };
        Client.RoomReady += OnRoomReady;
        Client.InventoryChanged += () => { if (Verbose) Console.WriteLine($"[{Name}] INVENTORY: " + string.Join(", ", Client.Inventory.ConvertAll(i => $"{Client.Resolve(i.NameRID)}#{i.ID}x{i.Count}"))); };
        Client.ObjectMoved += (id, x, y, sp) => { if (id == Client.PlayerId && walker != null && Dist(walker.X, walker.Y, x, y) > 2048f) { walker.Place(x, y); lastSentX = x; lastSentY = y; walking = false; } };
    }

    public static bool Verbose;

    public void Connect(string host, ushort port, string user, string pw, string newCharName = null)
    {
        Client.NewCharacterName = newCharName;
        Client.NewCharacterFemale = rng.Next(2) == 1;
        Client.NewCharacterBuild = new[] { "warrior", "mage", "hybrid" }[rng.Next(3)];
        Client.Connect(host, port, user, pw);
    }

    static float Dist(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    string FindRoomFile(string name)
    {
        if (name == null || !Directory.Exists(roomDir)) return null;
        foreach (var f in Directory.EnumerateFiles(roomDir))
            if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
        return null;
    }

    void OnRoomReady()
    {
        RoomsEntered++;
        string file = FindRoomFile(Client.Resolve(Client.Room.RoomFileRID));
        room = file != null ? RoomFile.Load(file) : null;
        walker = null; walking = false;
        var me = Client.Player;
        if (room != null && me != null)
        {
            float wx = GameClient.KodToWorld((ushort)me.Position2D.X), wy = GameClient.KodToWorld((ushort)me.Position2D.Y);
            walker = new Walker(room);
            if (!walker.Place(wx, wy)) { walker.X = wx; walker.Y = wy; walker.Z = 0; }
            lastSentX = wx; lastSentY = wy; lastAngle = 0xFFFF;
        }
        if (Verbose)
            foreach (var o in Client.Objects)
                Console.WriteLine($"[{Name}] obj {o.ID} nameRID={o.NameRID} -> '{Client.Resolve(o.NameRID)}' flags=0x{o.Flags.Value:X} attackable={o.Flags.IsAttackable} pos={o.Position2D}");
        if (Verbose)
            foreach (var o in Client.Objects)
                if (o.Flags.IsPlayer)
                {
                    Console.WriteLine($"[{Name}] PLAYER {o.ID} base overlay file={Client.Resolve(o.OverlayFileRID)} anim={o.Animation?.GetType().Name} group={(o.Animation as Meridian59.Data.Models.AnimationNone)?.Group} subOverlays={o.SubOverlays.Count}");
                    foreach (var so in o.SubOverlays)
                        Console.WriteLine($"[{Name}]    sub hotspot={so.HotSpot} file={Client.Resolve(so.ResourceID)} anim={so.Animation?.GetType().Name} group={(so.Animation as Meridian59.Data.Models.AnimationNone)?.Group} xlat={so.ColorTranslation} effect={so.Effect}");
                }
        enteredAt = DateTime.UtcNow;                                     // the server ignores actions in the first ~1 s (bSpam)
        idleUntil = enteredAt.AddSeconds(2 + rng.NextDouble() * 3);
        nextChat = enteredAt.AddSeconds(20 + rng.NextDouble() * 40);
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;
        float dt = (float)(now - lastTick).TotalSeconds; lastTick = now;
        Client.Update();
        if (!Ready || now < enteredAt.AddSeconds(2)) return;

        if (!walking)
        {
            if (unsentMove && (now - lastSend).TotalSeconds >= 1.0) SendPos(now);   // final position when we stop
            if (now >= idleUntil && !unsentMove) PickTarget();
        }
        else
        {
            float dx = targetX - walker.X, dy = targetY - walker.Y, d = MathF.Sqrt(dx * dx + dy * dy);
            float step = Walker.WalkSpeed * dt;
            if (d < 40f) { walking = false; unsentMove = true; idleUntil = now.AddSeconds(2 + rng.NextDouble() * 8); }
            else
            {
                bool moved = walker.Move(dx / d * Math.Min(step, d), dy / d * Math.Min(step, d));
                walker.UpdateZ(dt);
                if (moved) unsentMove = true;
                else { walking = false; idleUntil = now.AddSeconds(1 + rng.NextDouble() * 3); }   // blocked by a wall: pick another target
                if ((now - lastSend).TotalSeconds >= 1.0) SendPos(now);                          // official client sends at most 1 move/s
            }
        }

        if (now >= nextChat) { Client.SendSay(Lines[rng.Next(Lines.Length)]); ChatsSent++; nextChat = now.AddSeconds(45 + rng.NextDouble() * 90); }
    }

    void PickTarget()
    {
        double a = rng.NextDouble() * Math.PI * 2, r = (3 + rng.NextDouble() * 6) * 1024;
        targetX = walker.X + (float)(Math.Cos(a) * r); targetY = walker.Y + (float)(Math.Sin(a) * r);
        walking = true;
        ushort ang = (ushort)(((int)Math.Round(a / (Math.PI * 2) * 4096) + 4096) % 4096);
        if (ang != lastAngle && (DateTime.UtcNow - lastTurn).TotalSeconds >= 0.25) { Client.SendTurn(ang); lastAngle = ang; lastTurn = DateTime.UtcNow; }
    }

    void SendPos(DateTime now)
    {
        if (Dist(walker.X, walker.Y, lastSentX, lastSentY) < 8f) { unsentMove = false; return; }
        Client.SendMove(walker.X, walker.Y, false);
        lastSentX = walker.X; lastSentY = walker.Y; lastSend = now; unsentMove = false; MovesSent++;
    }

    public string Summary() => $"{Name}: state={Client.State} ({Client.Status}) rooms={RoomsEntered} moves={MovesSent} chats={ChatsSent}";
}
