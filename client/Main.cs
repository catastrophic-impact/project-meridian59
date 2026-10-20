using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Meridian59.Files.Room;
using Meridian59.Data.Models;

public partial class Main : Node
{
    string resDir;
    List<string> rooms = new List<string>();
    int roomIdx;
    TextureCache textures;
    Node3D current;
    Camera3D cam;
    Label hud;
    float yaw, pitch;
    string shotPath;
    int shotCountdown = -1;
    float optYaw = float.NaN, optPitch = float.NaN, optUp = 0, optScale = 0; bool optTestDrag, optInvTest, optPickTest, optItemTest; int itemTestStage; double itemTestAt; int pickTestStage; double pickTestAt; int invTestStage; double invTestAt; float shotDelay, refreshStatsAt; bool refreshedStats; int dragStage, dragFrame;
    bool wadePlaced; ulong wadeNext = 4000; string optEquip; double equipReadyAt; int optTab = -1; bool optMenu, optResetTest, optGoTest, optAttackTest, optSpellTest, optTargetTest, optMute, optRestTest, optProjTest; double projTestAt; string optCreateName; double createTestAt; double targetTestAt; int spellTestStage; double spellTestAt; int attackTestStage; double attackTestAt; double goTestAt;
    bool invActive;       // Space only toggles inventory items while the inventory was the last thing clicked
    uint targetIdVal; uint targetId { get => targetIdVal; set { if (targetIdVal != value) { targetIdVal = value; targetDirty = true; } } }   // object last clicked / tabbed to
    bool targetDirty; HudWindow targetWin; TextureRect targetPic; Label targetMissing, targetName, targetInfo; uint targetShown;         // test hook: open the Esc menu on entering the room
    string autoSay;       // test hook: say this once in the room
    static readonly double AutoWalkStart = double.TryParse(System.Environment.GetEnvironmentVariable("M59_WALKSTART"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var aws) ? aws : 0;
    static readonly double TurnAt = double.TryParse(System.Environment.GetEnvironmentVariable("M59_TURNAT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ta) ? ta : 0;
    static readonly float TurnYaw = float.TryParse(System.Environment.GetEnvironmentVariable("M59_TURNYAW"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ty) ? ty : 0;
    bool turnDone;
    float autoWalk = 0;   // test hook: hold 'forward' for this many seconds after entering the room

    // online mode
    GameClient client;
    SpriteCache sprites = new SpriteCache();
    GameAudio audio;
    Dictionary<string, string> filesByLowerName;
    Control loginPanel; ServerList servers; string serversPath;
    LineEdit edHost, edUser, edPass;
    Label loginStatus;
    string autoUser, autoPass, autoHost = "127.0.0.1";
    bool onlineMode, roomShown;
    RoomFile currentRoom;
    Node3D objectsRoot;

    // chat
    MiniMap miniMap;
    HudManager hudMgr; RichTextLabel chatLog; LineEdit chatInput; Control chatPanel; Label statusLabel; double statusAt; TextureRect facePic; Label faceMissing; Label mapRoomLabel; ProgressBar barHealth, barMana, barVigor; GridContainer invGrid; InvSlot selectedSlot; uint selectedItemId; Label invInfo; HudWindow itemWin, statusWin; TextureRect itemPic; Label itemName; RichTextLabel itemDesc; HBoxContainer lookActions; uint pendingInvLook; Label txtHealth, txtMana, txtVigor; bool chatFocused; double sayAt = -1; int sayStage = 0; int sayFrame;

    // walking
    Walker walker;
    bool optAdminWin;      // test hook: open the admin window on entering the game
    string optEquipTwo; double equipTwoAt; int equipTwoStage;
    string optLookAll; double lookAllAt;   // test hook: --lookall <item name>
    bool optThird;         // test hook: start in third person
    bool thirdPerson;      // V toggles; camera sits behind the player (wheel = distance)
    float camDist = 4f;    // Godot units (1 grid square = 4)
    Button adminMenuBtn;
    class ItemDef { public string Class, Name, Category; public bool Stack; }
    readonly List<ItemDef> adminItems = new List<ItemDef>();
    ItemList adminItemList; OptionButton adminCategory, adminTarget; LineEdit adminItemFilter; SpinBox adminCount;
    readonly List<ItemDef> adminItemsShown = new List<ItemDef>(); readonly List<uint> adminTargetIds = new List<uint>();
    readonly Queue<uint> pendingGiveTargets = new Queue<uint>();
    ItemList adminRoomList; LineEdit adminFilter; List<(int rid, string name, string file)> adminRooms = new List<(int, string, string)>(); List<int> adminShown = new List<int>();
    bool resting;          // R toggles rest / stand; walking is blocked while resting
    bool flyMode;                                   // F toggles free camera
    float lastSentX, lastSentY; double lastSendTime; ushort lastSentAngle = 0xFFFF; double lastTurnSendTime;
    bool movedSinceSend, lastRun;
    readonly Dictionary<uint, Sprite3D> objSprites = new Dictionary<uint, Sprite3D>();
    Walker objWalker; Meridian59.Files.Room.RoomFile objWalkerRoom;
    readonly Dictionary<uint, (float x, float y, float speed)> objTargets = new Dictionary<uint, (float, float, float)>();
    readonly Dictionary<uint, List<(float x, float y, bool lead)>> objQueue = new Dictionary<uint, List<(float, float, bool)>>();   // reported spots still to walk through, in order
    readonly Dictionary<uint, (float x, float y)> objPos = new Dictionary<uint, (float, float)>();
    // movement smoothing: servers only tell us where others were ~once per second, so we aim a little ahead of the last report
    // avatars: objects with overlays (players, equipped monsters) are composed from several bitmaps for the current viewing angle
    class AnimState { public RoomObject O; public bool Avatar; public int Bucket = -1, Key = int.MinValue; }
    readonly Dictionary<uint, AnimState> avatars = new Dictionary<uint, AnimState>();   // every object with a sprite: animation + view-angle state
    bool selfMoving; bool resyncRest; double restStandAt; uint rmbPick; float rmbMoved; int roomAnimLogged;
    List<RoomBuilder.RoomAnim> roomAnims = new List<RoomBuilder.RoomAnim>(); ulong roomAnimStart;
    static readonly string AnimDump = System.Environment.GetEnvironmentVariable("M59_ANIMDUMP");
    static readonly bool AnimLog = System.Environment.GetEnvironmentVariable("M59_ANIMLOG") == "1";
    readonly Dictionary<uint, ushort> objAngle = new Dictionary<uint, ushort>();
    readonly Dictionary<(uint, int, int), AvatarComposer.Result> avatarCache = new Dictionary<(uint, int, int), AvatarComposer.Result>();
    readonly Dictionary<uint, (float x, float y)> objReported = new Dictionary<uint, (float, float)>();
    static readonly float LeadSeconds = float.TryParse(System.Environment.GetEnvironmentVariable("M59_LEAD"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ld) ? ld : 0.4f;
    static readonly bool LagLog = System.Environment.GetEnvironmentVariable("M59_LAGLOG") == "1";
    float lagSum; int lagCount; double lagNext;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string startRoom = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--room" && i + 1 < args.Length) startRoom = args[++i];
            else if (args[i] == "--adminwin") optAdminWin = true;
            else if (args[i] == "--third") optThird = true;
            else if (args[i] == "--equiptwo" && i + 1 < args.Length) optEquipTwo = args[++i];
            else if (args[i] == "--lookall" && i + 1 < args.Length) optLookAll = args[++i];
            else if (args[i] == "--yaw" && i + 1 < args.Length) optYaw = float.Parse(args[++i]);
            else if (args[i] == "--pitch" && i + 1 < args.Length) optPitch = float.Parse(args[++i]);
            else if (args[i] == "--up" && i + 1 < args.Length) optUp = float.Parse(args[++i]);
            else if (args[i] == "--connect" && i + 2 < args.Length) { autoUser = args[++i]; autoPass = args[++i]; }
            else if (args[i] == "--host" && i + 1 < args.Length) autoHost = args[++i];
            else if (args[i] == "--autowalk" && i + 1 < args.Length) autoWalk = float.Parse(args[++i]);
            else if (args[i] == "--say" && i + 1 < args.Length) autoSay = args[++i];
            else if (args[i] == "--uiscale" && i + 1 < args.Length) optScale = float.Parse(args[++i]);
            else if (args[i] == "--testdrag") optTestDrag = true;
            else if (args[i] == "--menu") optMenu = true;
            else if (args[i] == "--gotest") optGoTest = true;
            else if (args[i] == "--attacktest") optAttackTest = true;
            else if (args[i] == "--targettest") optTargetTest = true;
            else if (args[i] == "--mute") optMute = true;
            else if (args[i] == "--projtest") optProjTest = true;
            else if (args[i] == "--resttest") optRestTest = true;
            else if (args[i] == "--createtest" && i + 1 < args.Length) optCreateName = args[++i];
            else if (args[i] == "--spelltest") optSpellTest = true;
            else if (args[i] == "--equip" && i + 1 < args.Length) optEquip = args[++i];
            else if (args[i] == "--tab" && i + 1 < args.Length) optTab = int.Parse(args[++i]);
            else if (args[i] == "--resettest") optResetTest = true;
            else if (args[i] == "--invtest") optInvTest = true;
            else if (args[i] == "--picktest") optPickTest = true;
            else if (args[i] == "--itemtest") optItemTest = true;
            else if (args[i] == "--refreshstats" && i + 1 < args.Length) refreshStatsAt = float.Parse(args[++i]);
            else if (args[i] == "--shotdelay" && i + 1 < args.Length) shotDelay = float.Parse(args[++i]);
            else if (args[i] == "--shot" && i + 1 < args.Length) shotPath = args[++i];
        }

        resDir = System.Environment.GetEnvironmentVariable("M59_RESOURCE");
        if (string.IsNullOrEmpty(resDir))
        {
            // game assets are not part of the repository: tools/extract_assets.py puts them in client/assets/resource (portable release) -
            // a developer checkout may still use the original client folder next to the project
            string a = ProjectSettings.GlobalizePath("res://assets/resource");
            resDir = Directory.Exists(a) ? a : ProjectSettings.GlobalizePath("res://../client_files/resource");
        }
        resDir = Path.GetFullPath(resDir);
        assetsMissing = !Directory.Exists(resDir);
        if (assetsMissing) { GD.PrintErr("Game assets not found: " + resDir + " (see docs/ASSETS.md, tools/extract_assets.py)"); Directory.CreateDirectory(resDir); }

        rooms = Directory.GetFiles(resDir).Where(f => f.EndsWith(".roo", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (rooms.Count == 0) { GD.PrintErr("No .roo files in " + resDir); GetTree().Quit(1); return; }
        textures = new TextureCache(resDir);

        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.05f, 0.05f, 0.08f) };
        AddChild(new WorldEnvironment { Environment = env });
        cam = new Camera3D { Far = 2000f, Near = 0.05f, Fov = 75 };
        AddChild(cam);

        var layer = new CanvasLayer();
        hud = new Label { Position = new Vector2(10, 44) };
        hud.AddThemeColorOverride("font_color", Colors.White);
        hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        hud.AddThemeConstantOverride("outline_size", 4);
        layer.AddChild(hud);
        AddChild(layer);

        roomIdx = 0;
        if (startRoom != null)
        {
            int i = rooms.FindIndex(r => Path.GetFileNameWithoutExtension(r).Equals(startRoom, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) roomIdx = i;
        }

        onlineMode = startRoom == null;      // `--room X` = offline room viewer; otherwise login screen
        if (onlineMode)
        {
            StartOnline();
        }
        else
        {
            LoadRoom();
            if (shotPath != null) shotCountdown = shotDelay > 0 ? (int)(shotDelay * 60) : 8;
        }
    }

    void LoadRoom()
    {
        current?.QueueFree();
        string path = rooms[roomIdx];
        string name = Path.GetFileName(path);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var room = RoomFile.Load(path);
            var b = new RoomBuilder(room, textures);
            current = b.Build(); roomAnims = b.Anims; roomAnimStart = Time.GetTicksMsec();
            AddChild(current);
            cam.Position = b.FindStart(out yaw);
            pitch = 0;
            if (!float.IsNaN(optYaw)) yaw = Mathf.DegToRad(optYaw);
            if (!float.IsNaN(optPitch)) pitch = Mathf.DegToRad(optPitch);
            cam.Position += Vector3.Up * optUp;
            ApplyLook();
            hud.Text = $"{name}  ({roomIdx + 1}/{rooms.Count})  v{room.Version}  walls={room.Walls.Count} sectors={room.Sectors.Count}  built in {sw.ElapsedMilliseconds}ms\n" +
                       "WASD move, Q/E down/up, Shift fast, mouse look (click to capture, Esc release), Tab/Shift+Tab next/prev room";
            GD.Print($"loaded {name} in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            hud.Text = $"{name}: FAILED {ex.Message}";
            GD.PrintErr($"{name}: {ex}");
        }
    }

    void ApplyLook() => cam.Rotation = new Vector3(pitch, yaw, 0);

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && onlineMode)
        {
            // hold the right mouse button to look around (the cursor stays visible otherwise, so windows are always clickable)
            if (mb.Pressed && !chatFocused && !OverHudWindow())
            {
                rmbPick = roomShown ? PickObjectAt(mb.Position) : 0; rmbMoved = 0;
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            else if (!mb.Pressed)
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
                if (rmbPick != 0 && rmbMoved < 6f) { GD.Print($"[pick] look at {rmbPick}"); client.LookAt(rmbPick); }   // a right CLICK (no mouse-look drag) opens the info window
                rmbPick = 0;
            }
        }
        else if (e is InputEventMouseButton mb2 && mb2.Pressed && !onlineMode) Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e is InputEventMouseButton mb3 && mb3.Pressed && mb3.ButtonIndex == MouseButton.Left && onlineMode && roomShown
                 && Input.MouseMode == Input.MouseModeEnum.Visible && !chatFocused && !OverHudWindow())
        {
            // left click: select the target only (the info window is opened by a right click); double click picks the object up
            uint hit = PickObjectAt(mb3.Position);
            if (hit != 0)
            {
                if (mb3.DoubleClick) { GD.Print($"[pick] pick up {hit}"); client.PickUp(hit); }
                else
                {
                    GD.Print($"[pick] target {hit}");
                    var ro = client.Objects.Find(x => x.ID == hit);
                    if (ro != null) { targetId = hit; AddChatLine("Target: " + (client.Resolve(ro.NameRID) ?? "?"), "System"); }
                }
            }
        }
        if (e is InputEventMouseButton mp && mp.Pressed && onlineMode && hudMgr != null && hudMgr.Get("inventory") is HudWindow invWin)
            invActive = invWin.Visible && invWin.GetGlobalRect().HasPoint(mp.GlobalPosition);
        if (e is InputEventMouseMotion mmr && Input.MouseMode == Input.MouseModeEnum.Captured) rmbMoved += mmr.Relative.Length();
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            yaw -= mm.Relative.X * 0.003f;
            pitch = Mathf.Clamp(pitch - mm.Relative.Y * 0.003f, -1.5f, 1.5f);
            ApplyLook();
        }
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (chatFocused) { if (k.Keycode == Key.Escape) { CloseChatInput(false); GetViewport().SetInputAsHandled(); } }
            else if (onlineMode && roomShown && (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter)) { OpenChatInput(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Escape) { if (onlineMode && roomShown) ToggleGameMenu(); else Input.MouseMode = Input.MouseModeEnum.Visible; }
            else if (k.Keycode == Key.F10 && hudMgr != null) hudMgr.Toggle("interface");
            else if (k.Keycode == Key.V && onlineMode && roomShown && !chatFocused && !k.CtrlPressed) { ToggleThirdPerson(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.F6 && onlineMode && roomShown && client.IsAdmin) { hudMgr?.Toggle("admin"); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.F9 && onlineMode && roomShown) { WhereReport(""); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Space && onlineMode && roomShown && !chatFocused)
            {
                if (invActive && packTabs.CurrentTab == 0 && selectedItemId != 0) ToggleUse(selectedItemId);
                else if (walker != null) client.GoThrough(walker.X, walker.Y);     // doors, stairs, portals
                GetViewport().SetInputAsHandled();
            }
            else if (k.Keycode == Key.F && onlineMode && roomShown && !chatFocused) { Attack(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.R && onlineMode && roomShown && !chatFocused && !k.CtrlPressed)
            { SetResting(!resting); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode >= Key.Key0 && k.Keycode <= Key.Key9 && onlineMode && roomShown && !chatFocused && !k.CtrlPressed)
            { ActivateHotbar(k.Keycode == Key.Key0 ? 9 : (int)(k.Keycode - Key.Key1)); GetViewport().SetInputAsHandled(); }
            else if (onlineMode && roomShown && !chatFocused && !k.CtrlPressed && (k.Keycode == Key.I || k.Keycode == Key.B || k.Keycode == Key.K))
            { ToggleTab(k.Keycode == Key.I ? 0 : k.Keycode == Key.B ? 1 : 2); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.F8 && onlineMode && roomShown) { flyMode = !flyMode; if (!flyMode && walker != null) UpdateWalkCamera(); }
            else if (k.Keycode == Key.Tab && onlineMode && roomShown && !chatFocused) { CycleTarget(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Tab && !onlineMode)
            {
                roomIdx = (roomIdx + (k.ShiftPressed ? rooms.Count - 1 : 1)) % rooms.Count;
                LoadRoom();
            }
        }
    }

    public override void _Process(double delta)
    {
        if (hud != null) hud.Visible = !onlineMode || (client != null && client.IsAdmin && audioSettings.ShowDebug);   // debug text: admins only, toggled in F6 > Debug
        if (onlineMode) { OnlineProcess(); UpdatePov(delta); UpdateScreenEffects((float)delta); UpdateBuffBar(); UpdateDebugLog(); UpdateFloatTexts((float)delta); TradeTestTick(); } else UpdateRoomAnimations();

        if (onlineMode && roomShown && currentRoom != null) WalkProcess((float)delta);
        if (autoSay != null && sayStage >= 0 && roomShown) DriveTypingTest();
        if (optGoTest && roomShown)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (goTestAt == 0) goTestAt = t;
            if (goTestAt > 0 && t - goTestAt > 1.5)
            {
                goTestAt = -1; GD.Print("[test] pressing Space");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
            }
        }
        if (optRestTest && roomShown && Time.GetTicksMsec() > 6000) { optRestTest = false; SetResting(true); GD.Print($"[test] resting={resting}"); restStandAt = Time.GetTicksMsec() + 8000; }
        if (restStandAt > 0 && Time.GetTicksMsec() > restStandAt && System.Environment.GetEnvironmentVariable("M59_RESTSTAND") == "1") { restStandAt = 0; SetResting(false); GD.Print($"[test] resting={resting}"); }
        if (System.Environment.GetEnvironmentVariable("M59_WADETEST") != null && roomShown && walker != null && Time.GetTicksMsec() > wadeNext && !wadePlaced)
        {   // test hook: stand in the deepest wading sector of this room (local only), M59_WADETEST=1 (any) or the depth number
            wadeNext = Time.GetTicksMsec() + 1500; int want = int.TryParse(System.Environment.GetEnvironmentVariable("M59_WADETEST"), out var wd) ? wd : 1;
            for (float x = 0; x < currentRoom.Width; x += 128)
                for (float y = 0; y < currentRoom.Height; y += 128)
                {
                    int sn = currentRoom.FindSector(x, y);
                    if (sn > 0 && (currentRoom.Sectors[sn - 1].Flags & 3) == want && walker.Place(x, y)) { wadePlaced = true; GD.Print($"[test] wading at {x},{y} sector {sn} depth {want} z {walker.Z}"); y = currentRoom.Height; x = currentRoom.Width; }
                }
        }
        if (optProjTest && roomShown && Time.GetTicksMsec() > 5000 && Time.GetTicksMsec() / 1000.0 > projTestAt)
        {   // test hook: fire a slow synthetic fireball from us to the nearest other object
            projTestAt = Time.GetTicksMsec() / 1000.0 + 100;
            uint rid = 0; foreach (var kv in client.Strings) if (string.Equals(kv.Value, "fireball.bgf", StringComparison.OrdinalIgnoreCase)) { rid = kv.Key; break; }
            uint near = 0; float best = float.MaxValue;
            foreach (var o in client.Objects) if (o.ID != client.PlayerId && objPos.TryGetValue(o.ID, out var op) && walker != null)
            {
                float ddx = op.x - walker.X, ddy = op.y - walker.Y, dd = ddx * ddx + ddy * ddy;
                float fwdDot = (ddx * -Mathf.Sin(yaw) + ddy * -Mathf.Cos(yaw)) / Mathf.Sqrt(Mathf.Max(dd, 1f));   // 1 = straight ahead
                if (dd > 1024f * 1024f * 9 && fwdDot > 0.92f && dd < best) { best = dd; near = o.ID; }
            }
            GD.Print($"[test] projectile rid {rid} -> object {near}");
            if (rid != 0 && near != 0)
                OnProjectile(new Projectile { OverlayFileRID = rid, Animation = new AnimationCycle(100, 1, 1), Source = new ObjectID(client.PlayerId), Target = new ObjectID(near), Speed = 1 });
        }
        if (optTargetTest && roomShown)
        {
            double tt = Time.GetTicksMsec() / 1000.0;
            if (targetTestAt == 0) targetTestAt = tt + 3;
            else if (targetTestAt > 0 && tt > targetTestAt) { targetTestAt = -1; CycleTarget(); GD.Print($"[test] Tab -> target {targetId}"); }
        }
        if (optAttackTest && roomShown)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (attackTestAt == 0) attackTestAt = t;
            if ((attackTestStage < 3 || System.Environment.GetEnvironmentVariable("M59_ATTACKLOOP") == "1") && t - attackTestAt > 1.5)
            {
                attackTestAt = t; attackTestStage++;
                foreach (var o in client.Objects) if (o.Flags.IsAttackable) GD.Print($"[test] attackable {o.ID} '{client.Resolve(o.NameRID)}' at {o.Position2D}");
                RoomObject tgt = null; float best = float.MaxValue;
                foreach (var o in client.Objects)
                    if (o.Flags.IsAttackable && o.ID != client.PlayerId && objPos.TryGetValue(o.ID, out var op) && walker != null)
                    { float dd = (op.x - walker.X) * (op.x - walker.X) + (op.y - walker.Y) * (op.y - walker.Y); if (dd < best) { best = dd; tgt = o; } }
                if (tgt != null) targetId = tgt.ID;
                GD.Print($"[test] pressing F (target {targetId})");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = false });
            }
        }
        if (optEquip != null && roomShown && client.Inventory.Count > 0 && (equipReadyAt == 0 ? (equipReadyAt = Time.GetTicksMsec() / 1000.0 + 2.0) < 0 : Time.GetTicksMsec() / 1000.0 > equipReadyAt))
        {
            bool forceToggle = optEquip.StartsWith("!");      // "!name" toggles even if already equipped
            var it = client.Inventory.Find(x => (client.Resolve(x.NameRID) ?? "") == optEquip.TrimStart('!'));
            optEquip = null;
            GD.Print($"[test] equipped set: {string.Join(",", client.Equipped)}; item {it?.ID}");
            if (it != null && (forceToggle || !client.Equipped.Contains(it.ID))) { GD.Print("[test] toggling " + it.ID); if (client.Equipped.Contains(it.ID)) client.UnuseItem(it.ID); else client.UseItem(it.ID); }
        }
        if (optEquipTwo != null && roomShown && client.Inventory.Count > 0 && Time.GetTicksMsec() / 1000.0 > (equipTwoAt == 0 ? (equipTwoAt = Time.GetTicksMsec() / 1000.0 + 2.5) : equipTwoAt))
        {   // test hook: use the FIRST item with this name, then 2 s later the LAST one (two maces)
            var all = client.Inventory.FindAll(x => (client.Resolve(x.NameRID) ?? "") == optEquipTwo);
            if (equipTwoStage == 0 && all.Count > 0) { GD.Print($"[test] use first #{all[0].ID}"); client.UseItem(all[0].ID); equipTwoStage = 1; equipTwoAt = Time.GetTicksMsec() / 1000.0 + 2.0; }
            else if (equipTwoStage == 1 && all.Count > 1) { GD.Print($"[test] use last #{all[^1].ID}; equipped now: {string.Join(",", client.Equipped)}"); client.UseItem(all[^1].ID); equipTwoStage = 2; }
        }
        if (optLookAll != null && roomShown && client.Inventory.Count > 0 && Time.GetTicksMsec() / 1000.0 > (lookAllAt == 0 ? (lookAllAt = Time.GetTicksMsec() / 1000.0 + 2.5) : lookAllAt))
        {   // test hook: ask the server to Look at every inventory item with this name and print the answers
            foreach (var it in client.Inventory.FindAll(x => (client.Resolve(x.NameRID) ?? "") == optLookAll)) { GD.Print($"[test] look at #{it.ID} flags=0x{it.Flags.Value:X}"); client.LookAt(it.ID); }
            optLookAll = null;
        }
        if (optSpellTest && roomShown && client.Spells.Count > 0)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (spellTestAt == 0) spellTestAt = t;
            bool loop = System.Environment.GetEnvironmentVariable("M59_SPELLLOOP") == "1";
            if ((spellTestStage == 0 || (loop && spellTestStage < 8 && t - spellTestAt > 4.0)) && t - spellTestAt > 1.0)
            {
                string want = System.Environment.GetEnvironmentVariable("M59_SPELLNAME");
                var sp = (want != null ? client.Spells.Find(s => (client.Resolve(s.NameRID) ?? "").ToLowerInvariant().Contains(want.ToLowerInvariant())) : null) ?? client.Spells[0];
                spellTestStage++; spellTestAt = t;
                GD.Print($"[test] assign '{client.Resolve(sp.NameRID)}' (targets {sp.TargetsCount}) to slot 2");
                AssignHotbar(1, new Godot.Collections.Dictionary { { "kind", "spell" }, { "id", (long)sp.ID }, { "name", client.Resolve(sp.NameRID) ?? "" } });
                targetId = client.PlayerId;
                if (System.Environment.GetEnvironmentVariable("M59_SPELLTARGET") == "nearest" && walker != null)
                {
                    float best = float.MaxValue;
                    foreach (var o in client.Objects)
                        if (o.Flags.IsAttackable && o.ID != client.PlayerId && objPos.TryGetValue(o.ID, out var op))
                        { float dd = (op.x - walker.X) * (op.x - walker.X) + (op.y - walker.Y) * (op.y - walker.Y); if (dd < best) { best = dd; targetId = o.ID; } }
                }
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key2, PhysicalKeycode = Key.Key2, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key2, PhysicalKeycode = Key.Key2, Pressed = false });
            }
        }
        if (optTestDrag && roomShown) DriveDragTest();
        if (optPickTest && roomShown) DrivePickTest();
        if (optItemTest && roomShown && invGrid != null && invGrid.GetChildCount() > 1)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (itemTestAt == 0) itemTestAt = t;
            if (itemTestStage == 0 && t - itemTestAt > 1.0)
            {   // select the 2nd item (torch) with a click, toggle it with the real Space key, then right-click the apple
                var torch = (InvSlot)invGrid.GetChild(1); torch.Clicked?.Invoke(torch);
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
                itemTestStage = 1; itemTestAt = t; GD.Print($"[test] selected '{torch.ItemName}' and pressed Space");
            }
            else if (itemTestStage == 1 && t - itemTestAt > 1.0)
            {
                var apple = (InvSlot)invGrid.GetChild(invGrid.GetChildCount() - 2); apple.RightClicked?.Invoke(apple);
                itemTestStage = 2; GD.Print($"[test] right-clicked '{apple.ItemName}'");
            }
        }
        if (optInvTest && roomShown && invGrid != null && invGrid.GetChildCount() > 0)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (invTestAt == 0) invTestAt = t;
            if (invTestStage == 0 && t - invTestAt > 1.0)
            {   // equip the first item (double-click handler) and drop the last one (what dropping onto the world does)
                var first = (InvSlot)invGrid.GetChild(0);
                var last = (InvSlot)invGrid.GetChild(invGrid.GetChildCount() - 1);
                GD.Print($"[test] use '{first.ItemName}', drop '{last.ItemName}'");
                first.Activated?.Invoke(first);
                client.DropItem(last.ItemId, (uint)last.Count);
                invTestStage = 1; invTestAt = t;
            }
            else if (invTestStage == 1 && t - invTestAt > 1.5)
            {   // pick the dropped stack back up
                foreach (var o in client.Objects)
                    if ((client.Resolve(o.NameRID) ?? "") == "mushroom") { GD.Print($"[test] picking up dropped object {o.ID} count={o.Count}"); client.PickUp(o.ID, o.Count); break; }
                invTestStage = 2;
            }
        }
        if (refreshStatsAt > 0 && !refreshedStats && roomShown && Time.GetTicksMsec() / 1000.0 > refreshStatsAt) { refreshedStats = true; client.RequestStats(Meridian59.Common.Enums.StatGroup.Condition); }
        if (roomShown && Time.GetTicksMsec() / 1000.0 > statusAt) { statusAt = Time.GetTicksMsec() / 1000.0 + 0.25; UpdateStatusWindow(); }
        bool freeCam = (!onlineMode || flyMode) && !chatFocused;

        float speed = (Input.IsKeyPressed(Key.Shift) ? 60f : 15f) * (float)delta;
        var dir = Vector3.Zero;
        var basis = cam.GlobalTransform.Basis;
        if (Input.IsKeyPressed(Key.W)) dir -= basis.Z;
        if (Input.IsKeyPressed(Key.S)) dir += basis.Z;
        if (Input.IsKeyPressed(Key.A)) dir -= basis.X;
        if (Input.IsKeyPressed(Key.D)) dir += basis.X;
        if (Input.IsKeyPressed(Key.E)) dir += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) dir += Vector3.Down;
        if (freeCam && dir != Vector3.Zero) cam.Position += dir.Normalized() * speed;

        if (shotCountdown > 0 && --shotCountdown == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(shotPath);
            GD.Print("saved " + shotPath);
            System.Environment.Exit(0);   // hard exit: Godot's normal quit sometimes hangs on this machine
        }
    }

    // ------------------------------------------------------------------ online mode
    string ResolveFile(string name)
    {
        if (filesByLowerName == null)
        {
            filesByLowerName = new Dictionary<string, string>();
            foreach (var f in Directory.GetFiles(resDir)) filesByLowerName[Path.GetFileName(f).ToLowerInvariant()] = f;
        }
        return name != null && filesByLowerName.TryGetValue(name.ToLowerInvariant(), out var p) ? p : null;
    }

    void StartOnline()
    {
        serversPath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/servers.json"));
        servers = ServerList.Load(serversPath);
        client = new GameClient();
        client.Log += s => GD.Print("[client] " + s);
        client.RoomReady += OnRoomReady;
        client.ChatReceived += (text, kind) => AddChatLine(text, kind);
        client.ObjectAdded += o => { if (roomShown && currentRoom != null) AddObjectSprite(o); };
        audio = new GameAudio { ResolveFile = ResolveFile };
        AddChild(audio);
        audio.ObjectPos = id => objPos.TryGetValue(id, out var p) ? RoomBuilder.P(p.x, p.y, 0) : (Vector3?)null;
        audio.ListenerPos = () => walker != null ? RoomBuilder.P(walker.X, walker.Y, 0) : (Vector3?)null;
        LoadAudioSettings();
        client.SectorMoved += OnSectorMoved;
        client.WallAnimated += OnWallAnimated;
        client.LightingChanged += (a, v) => { RoomLight.Set(a, v); if (AnimLog) GD.Print($"[light] ambient {a} viewer {v}"); };
        client.SunChanged += (i, a) => { RoomLight.SetSun(i, a); if (AnimLog) GD.Print($"[light] sun intensity {i} angle {a}"); };
        client.SoundRequested += (n, o, f, r, c, rad, mv) => { if (!optMute) audio.PlaySound(n, o, f, r, c, rad, mv); };
        client.MusicRequested += n => { if (!optMute) audio.PlayMusic(n); };
        client.RoomChanging += () => { ClearPendingRoomChanges(); audio.RoomChanged(); ClearProjectiles(); ClearBgOverlays(); resyncRest = true; };
        client.ProjectileFired += OnProjectile;
        client.BackgroundChanged += SetBackground;
        client.BackgroundOverlayChanged += OnBackgroundOverlay;
        client.EffectReceived += OnEffect;
        client.CharCreationRequested += ci => ShowCharCreate(ci);
        client.CharCreationRejected += OnCharCreationRejected;
        client.ObjectRemoved += id => { if (objSprites.Remove(id, out var n)) n.QueueFree(); objTargets.Remove(id); objQueue.Remove(id); objPos.Remove(id); objReported.Remove(id); avatars.Remove(id); objAngle.Remove(id); };
        client.ObjectMoved += (id, x, y, spd) =>
        {
            if (id == client.PlayerId)
            {   // server moved/corrected us: snap if it differs noticeably
                if (walker != null && Mathf.Abs(walker.X - x) + Mathf.Abs(walker.Y - y) > 512f) { walker.Place(x, y); lastSentX = x; lastSentY = y; }
                return;
            }
            OnOtherMoved(id, x, y, spd);
        };
        client.ObjectTurned += (id, ang) => { objAngle[id] = ang; };
        client.ObjectChanged += id =>
        {
            foreach (var key in new List<(uint, int, int)>(avatarCache.Keys)) if (key.Item1 == id) avatarCache.Remove(key);
            var obj = client.Objects.Find(o => o.ID == id);
            if (System.Environment.GetEnvironmentVariable("M59_TRACE") == "1" && obj != null)
                GD.Print($"[change] #{id} {client.Resolve(obj.NameRID)} subOverlays={obj.SubOverlays.Count}: " + string.Join(", ", obj.SubOverlays.ConvertAll(so => $"{so.HotSpot}:{client.Resolve(so.ResourceID)}")));
            if (obj != null && objSprites.ContainsKey(id)) avatars[id] = new AnimState { O = obj, Avatar = obj.SubOverlays.Count > 0 || obj.MotionSubOverlays.Count > 0 };   // re-evaluate at the next frame
        };
        var povCanvas = new CanvasLayer { Layer = 1 };
        povLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        povLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        povCanvas.AddChild(povLayer); AddChild(povCanvas);
        BuildEffectsLayer(); BuildBuffBar();
        BuildLoginPanel();
        BuildGameMenu();
        hud.Text = "";
        if (autoUser != null) DoConnect(autoHost, 5959, autoUser, autoPass, ProfileStrings(servers.Servers[0]));
        else if (shotPath != null) shotCountdown = 10;   // screenshot of the login screen itself
    }

    string ProfileStrings(ServerProfile p)
    {
        string f = p.Strings ?? "";
        if (f == "") return "";
        if (Path.IsPathRooted(f)) return f;
        string full = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../" + f));
        if (!File.Exists(full) && f.StartsWith("client_files/"))      // portable release: the string tables of other servers live with the extracted assets
        {
            string q = Path.GetFullPath(ProjectSettings.GlobalizePath("res://assets/" + f.Substring("client_files/".Length)));
            if (File.Exists(q)) return q;
        }
        return full;
    }
    bool assetsMissing;

    void BuildLoginPanel()
    {
        var layer = new CanvasLayer { Layer = 10 };
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var title = new Label { Text = "Meridian 59", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);

        var pick = new OptionButton();
        for (int i = 0; i < servers.Servers.Count; i++) pick.AddItem(servers.Servers[i].Name, i);
        var edName = new LineEdit { PlaceholderText = "server name" };
        edHost = new LineEdit { PlaceholderText = "address:port" };
        var edStrings = new LineEdit { PlaceholderText = "string file (rsc0000.rsb of that server)" };
        edUser = new LineEdit { PlaceholderText = "account name" };
        edPass = new LineEdit { PlaceholderText = "password", Secret = true };
        void fill(int idx)
        {
            if (idx < 0 || idx >= servers.Servers.Count) return;
            var sp = servers.Servers[idx];
            edName.Text = sp.Name; edHost.Text = sp.Host + ":" + sp.Port; edStrings.Text = sp.Strings; edUser.Text = autoUser ?? sp.User;
            servers.Selected = idx;
        }
        pick.Selected = servers.Selected; fill(servers.Selected);
        pick.ItemSelected += i => { fill((int)i); loginStatus.Text = ""; };

        void parseHost(out string host, out ushort port)
        {
            host = edHost.Text.Trim(); port = 5959;
            int c = host.LastIndexOf(':');
            if (c > 0 && ushort.TryParse(host.Substring(c + 1), out var pp)) { port = pp; host = host.Substring(0, c); }
        }
        ServerProfile snapshot()
        {
            parseHost(out var h, out var pt);
            return new ServerProfile { Name = edName.Text.Trim() == "" ? h : edName.Text.Trim(), Host = h, Port = pt, Strings = edStrings.Text.Trim(), User = edUser.Text.Trim() };
        }

        var connectBtn = new Button { Text = "Connect" };
        var saveBtn = new Button { Text = "Save server", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var delBtn = new Button { Text = "Delete", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var quitBtn = new Button { Text = "Quit", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        loginStatus = new Label { Text = assetsMissing ? "Game assets not found: run tools/extract_assets.py (see docs/ASSETS.md) and restart." : "", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };

        box.AddChild(new Label { Text = "Server" }); box.AddChild(pick); box.AddChild(edName); box.AddChild(edHost); box.AddChild(edStrings);
        box.AddChild(new Label { Text = "Account" }); box.AddChild(edUser); box.AddChild(edPass);
        box.AddChild(connectBtn);
        var row = new HBoxContainer(); row.AddChild(saveBtn); row.AddChild(delBtn); row.AddChild(quitBtn);
        box.AddChild(row); box.AddChild(loginStatus);
        margin.AddChild(box); panel.AddChild(margin); center.AddChild(panel); layer.AddChild(center);
        AddChild(layer);
        loginPanel = center;

        void connect()
        {
            var sp = snapshot();
            // remember the last used account name (never the password)
            int idx = servers.Servers.FindIndex(x => x.Name == sp.Name);
            if (idx >= 0) { servers.Servers[idx].User = sp.User; servers.Selected = idx; servers.Save(serversPath); }
            DoConnect(sp.Host, (ushort)sp.Port, sp.User, edPass.Text, ProfileStrings(sp));
        }
        connectBtn.Pressed += connect;
        edPass.TextSubmitted += _ => connect();
        saveBtn.Pressed += () =>
        {
            var sp = snapshot();
            int idx = servers.Servers.FindIndex(x => x.Name == sp.Name);
            if (idx >= 0) { sp.Builtin = servers.Servers[idx].Builtin; servers.Servers[idx] = sp; }
            else { servers.Servers.Add(sp); idx = servers.Servers.Count - 1; pick.AddItem(sp.Name, idx); }
            servers.Selected = idx; pick.Selected = idx; servers.Save(serversPath);
            loginStatus.Text = "Saved \"" + sp.Name + "\"";
        };
        delBtn.Pressed += () =>
        {
            int idx = pick.Selected;
            if (idx < 0 || idx >= servers.Servers.Count) return;
            if (servers.Servers[idx].Builtin) { loginStatus.Text = "Built-in servers cannot be deleted"; return; }
            servers.Servers.RemoveAt(idx); pick.RemoveItem(idx);
            servers.Selected = 0; pick.Selected = 0; fill(0); servers.Save(serversPath);
        };
        quitBtn.Pressed += QuitGame;
    }

    void DoConnect(string host, ushort port, string user, string pass, string stringsFile)
    {
        roomShown = false;
        if (stringsFile == "" || !File.Exists(stringsFile))
        {
            loginStatus.Text = stringsFile == "" ? "This server needs a string file (its rsc0000.rsb)." : "String file not found:\n" + stringsFile;
            return;
        }
        if (!client.LoadStrings(stringsFile)) { loginStatus.Text = client.Status; return; }
        client.Connect(host, port, user, pass);
    }

    // ---- Esc menu: resume / settings / disconnect / quit
    Control gameMenu;
    void BuildGameMenu()
    {
        var layer = new CanvasLayer { Layer = 100 };
        var center = new CenterContainer { Visible = false };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(260, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.07f, 0.07f, 0.10f, 1f), BorderColor = new Color(0.4f, 0.4f, 0.55f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        box.AddChild(new Label { Text = "Menu", HorizontalAlignment = HorizontalAlignment.Center });
        Button mk(string t, Action a) { var b = new Button { Text = t }; b.Pressed += a; box.AddChild(b); return b; }
        mk("Resume", () => ToggleGameMenu(false));
        mk("Spells (B)", () => { ToggleGameMenu(false); if (hudMgr != null) ToggleTab(1); });
        mk("Skills (K)", () => { ToggleGameMenu(false); if (hudMgr != null) ToggleTab(2); });
        mk("Camera: 1st / 3rd person (V)", () => { ToggleGameMenu(false); if (walker != null) ToggleThirdPerson(); });
        mk("Interface settings", () => { ToggleGameMenu(false); hudMgr?.Toggle("interface"); });
        adminMenuBtn = mk("Admin: teleport (F6)", () => { ToggleGameMenu(false); hudMgr?.Toggle("admin"); });
        adminMenuBtn.Visible = false;                    // shown after login when the account is an admin
        mk("Disconnect", () => ReturnToLogin("Disconnected."));
        mk("Quit game", QuitGame);
        margin.AddChild(box); panel.AddChild(margin); center.AddChild(panel); layer.AddChild(center);
        AddChild(layer);
        gameMenu = center;
    }

    void ToggleGameMenu(bool? show = null)
    {
        if (gameMenu == null) return;
        if (adminMenuBtn != null) adminMenuBtn.Visible = client.IsAdmin;
        gameMenu.Visible = show ?? !gameMenu.Visible;
        if (gameMenu.Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Leave the game world and show the server screen again.</summary>
    void ReturnToLogin(string message)
    {
        client.Disconnect(); HideCharCreate(); resting = false; UpdateRestButton(); ResetEffects(); SetBackground(null); ClearBgOverlays();
        roomShown = false; flyMode = false; walker = null; currentRoom = null;
        current?.QueueFree(); current = null;
        objectsRoot?.QueueFree(); objectsRoot = null;
        objSprites.Clear(); objTargets.Clear(); objQueue.Clear(); objPos.Clear(); objReported.Clear(); avatars.Clear(); objAngle.Clear(); avatarCache.Clear();
        if (hudMgr != null) hudMgr.Root.Visible = false;
        if (buffRow != null) { buffRow.Visible = false; buffsDirty = true; }
        SetMapMaximized(false); dbgLast.Clear();
        hud.Text = "";
        ToggleGameMenu(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        loginPanel.Visible = true;
        loginStatus.Text = message;
    }

    void QuitGame()
    {
        client?.Disconnect();
        System.Environment.Exit(0);   // Godot's normal quit sometimes hangs on this machine
    }

    void OnlineProcess()
    {
        client.Update();
        if (createPanel != null && optCreateName != null)
        {
            double ct = Time.GetTicksMsec() / 1000.0;
            if (createTestAt == 0) createTestAt = ct + 2;
            else if (createTestAt > 0 && ct > createTestAt) { createTestAt = -1; ccName.Text = optCreateName; ccFemale = true; ccHair = 2; ccEyes = 1; RefreshCreateLooks(); foreach (var sk in ccSkills) sk.box.ButtonPressed = true; SubmitCharacter(); }
        }
        if (loginPanel != null && !roomShown) loginStatus.Text = client.Status;
        if (createPanel != null && client.State == GameClient.Phase.Failed) ReturnToLogin("Connection lost: " + client.Status);
        if (roomShown && client.State == GameClient.Phase.Failed && shotPath == null) ReturnToLogin("Connection lost: " + client.Status);
        if (client.State == GameClient.Phase.Failed && shotPath != null && shotCountdown < 0)
        {
            GD.PrintErr("online failed: " + client.Status);
            System.Environment.Exit(2);
        }
    }

    void DumpAvatarSheet()
    {
        string dir = System.Environment.GetEnvironmentVariable("M59_AVATAR_SHEET");
        if (dir == null) return;
        var o = client.Objects.Find(x => x.Flags.IsPlayer && x.ID == client.PlayerId) ?? client.Objects.Find(x => x.Flags.IsPlayer);
        if (System.Environment.GetEnvironmentVariable("M59_AVATAR_OBJ") == "companion") o = client.Objects.Find(x => IsCompanion(x.ID)) ?? o;
        bool motion = System.Environment.GetEnvironmentVariable("M59_AVATAR_MOTION") == "1";
        if (o == null) { GD.Print("[sheet] no player object"); return; }
        Directory.CreateDirectory(dir);
        for (int k = 0; k < 8; k++)
        {
            var res = AvatarComposer.Compose(o, (ushort)(k * 512), rid => ResolveFile(client.Resolve(rid)), motion);
            if (res?.Texture == null) continue;
            var img = res.Texture.GetImage(); img.Resize(img.GetWidth() * 3, img.GetHeight() * 3, Image.Interpolation.Nearest);
            img.SavePng(Path.Combine(dir, $"rel{k * 512:D4}.png"));
        }
        GD.Print("[sheet] written to " + dir);
    }

    void OnRoomReady()
    {
        var info = client.Room;
        SetBackground(info != null && info.BackgroundFileRID != 0 ? client.Resolve(info.BackgroundFileRID) : null);   // sky of outdoor rooms (room info carries it; BP_BACKGROUND changes it later)
        string roomName = client.Resolve(info.RoomFileRID);
        string path = ResolveFile(roomName);
        if (path == null) { loginStatus.Text = $"Room file '{roomName}' not found in {resDir}"; GD.PrintErr(loginStatus.Text); return; }

        current?.QueueFree();
        objectsRoot?.QueueFree();
        ClearLifts();
        currentRoom = RoomFile.Load(path);
        var b = MakeBuilder(currentRoom);
        current = b.Build(); roomAnims = b.Anims; roomAnimStart = Time.GetTicksMsec();
        if (AnimLog) GD.Print($"[roomanim] {roomAnims.Count} animated materials: {roomAnims.FindAll(a => a.Cycle).Count} cycling, {roomAnims.FindAll(a => !a.Cycle).Count} scrolling");
        if (AnimLog) foreach (var a in roomAnims) GD.Print($"[roomanim]   texture {a.Bmp} {(a.Cycle ? "cycle" : "scroll")} {(a.Wall ? "wall" : "flat")} period {a.PeriodMs:F0} ms dx {a.Dx} dy {a.Dy}");
        AddChild(current);

        // objects as billboards
        objectsRoot = new Node3D { Name = "Objects" };
        AddChild(objectsRoot);
        objSprites.Clear(); objTargets.Clear(); objQueue.Clear(); objPos.Clear(); objReported.Clear(); avatars.Clear(); objAngle.Clear(); avatarCache.Clear();
        walker = null;
        foreach (var o in client.Objects)
        {
            float wx = GameClient.KodToWorld((ushort)o.Position2D.X), wy = GameClient.KodToWorld((ushort)o.Position2D.Y);
            if (o.ID == client.PlayerId)
            {
                walker = new Walker(currentRoom);
                if (!walker.Place(wx, wy)) { walker.X = wx; walker.Y = wy; walker.Z = 0; }
                lastSentX = wx; lastSentY = wy; lastSentAngle = 0xFFFF; movedSinceSend = false;
                float ang = o.AngleUnits / 4096f * Mathf.Tau;
                yaw = -ang - Mathf.Pi / 2f; pitch = 0;    // world y points south, so turning right = angle grows = yaw shrinks
                if (!float.IsNaN(optYaw)) yaw = Mathf.DegToRad(optYaw);
                if (!float.IsNaN(optPitch)) pitch = Mathf.DegToRad(optPitch);
                flyMode = false;
                if (optThird) thirdPerson = true;
                if (System.Environment.GetEnvironmentVariable("M59_AVATAR_OBJ") != null) GetTree().CreateTimer(9).Timeout += DumpAvatarSheet; else DumpAvatarSheet();
                UpdateWalkCamera();
                if (AddObjectSprite(o) && objSprites.TryGetValue(o.ID, out var mySp)) mySp.Visible = thirdPerson;   // own avatar, only shown in third person
                continue;
            }
            AddObjectSprite(o);
        }
        int shown = objSprites.Count;

        roomShown = true;
        ApplyPendingRoomChanges();
        loginPanel.Visible = false; HideCharCreate();
        GetViewport().GuiReleaseFocus();
        if (chatPanel == null) BuildHud();
        if (!client.IsAdmin) { var aw = hudMgr?.Get("admin"); if (aw != null && aw.Visible) hudMgr.Toggle("admin"); }   // a saved layout must not show admin tools to a normal account
        if (optAdminWin && client.IsAdmin) { optAdminWin = false; if (hudMgr?.Get("admin")?.Content is TabContainer tc0) { tc0.CurrentTab = 1; RefreshAdminTargets(); } var aw0 = hudMgr?.Get("admin"); if (aw0 != null && !aw0.Visible) hudMgr.Toggle("admin"); if (adminFilter != null) adminFilter.Text = "cor"; FillAdminList(); }
        hudMgr.Root.Visible = true;
        foreach (var wn in (System.Environment.GetEnvironmentVariable("M59_OPENWIN") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) { var hw = hudMgr.Get(wn); if (hw != null && !hw.Visible) hudMgr.Toggle(wn); }   // test hook
        if (buffRow != null) buffRow.Visible = true;
        if (miniMap != null) miniMap.SavePath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/map_" + (client.CharacterName ?? "x").Replace('/', '_') + ".json"));
        miniMap?.SetRoom(currentRoom, Path.GetFileName(path));
        if (System.Environment.GetEnvironmentVariable("M59_MAPMAX") == "1") CallDeferred(nameof(SetMapMaximized), true);
        if (optMenu) ToggleGameMenu(true);
        if (optTab >= 0) packTabs.CurrentTab = optTab;
        if (optResetTest) hudMgr.ResetLayout();
        UpdateFace();
        
        string rn = client.Resolve(info.RoomNameRID) ?? "?";
        hud.Text = $"{rn}  [{Path.GetFileName(path)}]  objects={client.Objects.Count} (sprites {shown})  ambient={info.AmbientLight}\n" +
                   "WASD walk, Shift run, hold right mouse button or arrows to turn, F8 = free camera, F = attack, Space = go through door";
        GD.Print($"[pos] walker={walker?.X},{walker?.Y},{walker?.Z} yaw={yaw}");
        GD.Print($"online room: {rn} ({Path.GetFileName(path)}), {client.Objects.Count} objects, {shown} sprites");
        if (shotPath != null && shotCountdown < 0) shotCountdown = autoWalk > 0 ? (int)(autoWalk * 60) + 40 : (shotDelay > 0 ? (int)(shotDelay * 60) : (autoSay != null ? 260 : (optTestDrag ? 220 : 10)));
    }

    bool AddObjectSprite(RoomObject o)
    {
        float wx = GameClient.KodToWorld((ushort)o.Position2D.X), wy = GameClient.KodToWorld((ushort)o.Position2D.Y);
        var spritePath = ResolveFile(client.Resolve(o.OverlayFileRID));
        if (spritePath == null) return false;
        ushort group = (o.Animation is Meridian59.Data.Models.AnimationNone an) ? an.Group : (ushort)1;
        if (group == 0) group = 1;
        var e = sprites.Get(spritePath, group, 0, o.ColorTranslation);
        if (e == null) return false;
        float pix = 16f * RoomBuilder.S / e.Shrink;     // 1 bitmap pixel = 1 kod unit = 16 fineness
        var sp = new Sprite3D
        {
            Texture = e.Texture,
            PixelSize = pix,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Shaded = false,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            DoubleSided = true,
            Offset = new Vector2(e.XOffset, e.Height / 2f - e.YOffset),     // official: centred on the object + XOffset, bottom edge at floor - YOffset
            Name = "obj" + o.ID,
        };
        objectsRoot.AddChild(sp);
        AddNameLabel(sp, o, e.Height * pix);
        objSprites[o.ID] = sp;
        objPos[o.ID] = (wx, wy);
        avatars[o.ID] = new AnimState { O = o, Avatar = o.SubOverlays.Count > 0 || o.MotionSubOverlays.Count > 0 };
        objAngle[o.ID] = o.AngleUnits;
        PlaceSprite(o.ID);
        return true;
    }

    /// <summary>Another object moved. Aim slightly ahead of the reported spot along its direction of travel (checked against walls),
    /// so the sprite is not a whole update interval behind. LeadSeconds = 0 gives the plain "glide to last report" behaviour.</summary>
    void OnOtherMoved(uint id, float x, float y, float spd)
    {
        float speed = Mathf.Max(spd, 12f) * 102.4f;
        DbgMoveReport(id, x, y, spd);
        if (LagLog && objPos.TryGetValue(id, out var cur)) { lagSum += Mathf.Sqrt((cur.x - x) * (cur.x - x) + (cur.y - y) * (cur.y - y)) / 1024f; lagCount++; }
        float tx = x, ty = y; bool haveLead = false;
        bool jump = objReported.TryGetValue(id, out var last) && Mathf.Sqrt((x - last.x) * (x - last.x) + (y - last.y) * (y - last.y)) > 4096f;   // teleport / room change: no path to follow
        if (LeadSeconds > 0 && !jump && objReported.TryGetValue(id, out var prev) && currentRoom != null)
        {
            float dx = x - prev.x, dy = y - prev.y, d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > 64f && d < speed * 3f)            // a real step (not a teleport / jitter)
            {
                var w = new Walker(currentRoom);
                if (w.Place(x, y)) { w.Move(dx / d * speed * LeadSeconds, dy / d * speed * LeadSeconds); tx = w.X; ty = w.Y; haveLead = true; }
            }
        }
        objReported[id] = (x, y);
        if (jump || spd <= 0f) { objPos[id] = (x, y); objTargets.Remove(id); if (objQueue.TryGetValue(id, out var qq)) qq.Clear(); PlaceSprite(id); return; }   // teleport (speed 0): appear there instead of gliding through walls
        // The server walks cell by cell, possibly around corners: walk through every reported spot in order instead of
        // cutting straight to the newest one.  The lead guess is only a temporary tail, replaced by the next report.
        if (!objQueue.TryGetValue(id, out var q)) objQueue[id] = q = new List<(float, float, bool)>();
        if (jump || !objPos.ContainsKey(id)) { q.Clear(); objTargets.Remove(id); }
        if (q.Count > 0 && q[q.Count - 1].lead) q.RemoveAt(q.Count - 1);
        q.Add((x, y, false));
        if (haveLead) q.Add((tx, ty, true));
        while (q.Count > 8) q.RemoveAt(0);
        if (!objTargets.ContainsKey(id)) { var h = q[0]; q.RemoveAt(0); objTargets[id] = (h.x, h.y, speed); }
        else objTargets[id] = (objTargets[id].x, objTargets[id].y, speed);
    }

    // ---- admin tools (only offered when the login result says this account is an admin) ----
    void BuildAdminWindow()
    {
        foreach (var p in new[] { "res://../server/rooms.json", "res://../userdata/rooms.json" })
        {
            string path = ProjectSettings.GlobalizePath(p);
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                foreach (var r in doc.RootElement.EnumerateArray())
                    adminRooms.Add((r.GetProperty("rid").GetInt32(), r.GetProperty("name").GetString(), r.TryGetProperty("file", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String ? f.GetString() : ""));
                break;
            }
            catch (Exception ex) { GD.PrintErr("rooms.json: " + ex.Message); }
        }
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        adminFilter = new LineEdit { PlaceholderText = "Filter rooms (name or number) - double-click to teleport", ClearButtonEnabled = true };
        adminFilter.TextChanged += _ => FillAdminList();
        adminFilter.FocusEntered += () => { chatFocused = true; };      // typing here must not walk the character
        adminFilter.FocusExited += () => { chatFocused = false; };
        adminRoomList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single };
        adminRoomList.ItemActivated += idx => { if (idx >= 0 && idx < adminShown.Count) AdminTeleport(adminShown[(int)idx]); };
        var go = new Button { Text = "Teleport to selected" };
        go.Pressed += () => { var sel = adminRoomList.GetSelectedItems(); if (sel.Length > 0) AdminTeleport(adminShown[sel[0]]); };
        box.AddChild(adminFilter); box.AddChild(adminRoomList); box.AddChild(go);
        if (adminRooms.Count == 0) box.AddChild(new Label { Text = "rooms.json not found (run tools/make_roomlist.py)" });
        box.Name = "Teleport";
        var tabs = new TabContainer();
        tabs.AddChild(box);
        tabs.AddChild(BuildItemsTab());
        tabs.AddChild(BuildDebugTab());
        tabs.TabChanged += t => { if (t == 1) RefreshAdminTargets(); };
        client.AdminObjectCreated += id => { if (pendingGiveTargets.Count > 0) client.SendAdmin($"send object {pendingGiveTargets.Dequeue()} NewHold what object {id}"); };
        hudMgr.Add("admin", "Admin tools", tabs, new Vector2(0.60f, 0.10f), new Vector2(380, 460), visibleByDefault: false);
        FillAdminList();
    }

    Control BuildDebugTab()
    {
        var box = new VBoxContainer { Name = "Debug" };
        box.AddThemeConstantOverride("separation", 8);
        var show = new CheckButton { Text = "Show debug text (top left)", ButtonPressed = audioSettings.ShowDebug, TooltipText = "Room name and file, object count, ambient light and the key hints. Admin accounts only." };
        show.Toggled += on => { audioSettings.ShowDebug = on; SaveAudioSettings(); };
        box.AddChild(show);
        box.AddChild(new Label { Text = "Debug options for admin accounts. The setting is remembered.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) });
        return box;
    }

    Control BuildItemsTab()
    {
        foreach (var p in new[] { "res://../server/items.json", "res://../userdata/items.json" })
        {
            string path = ProjectSettings.GlobalizePath(p);
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                foreach (var r in doc.RootElement.EnumerateArray())
                    adminItems.Add(new ItemDef { Class = r.GetProperty("class").GetString(), Name = r.GetProperty("name").GetString(), Category = r.GetProperty("category").GetString(), Stack = r.GetProperty("stack").GetBoolean() });
                break;
            }
            catch (Exception ex) { GD.PrintErr("items.json: " + ex.Message); }
        }
        var box = new VBoxContainer { Name = "Items" };
        box.AddThemeConstantOverride("separation", 4);
        var top = new HBoxContainer();
        adminCategory = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        adminCategory.AddItem("All categories");
        var cats = new SortedSet<string>(); foreach (var i in adminItems) cats.Add(i.Category);
        foreach (var c in cats) adminCategory.AddItem(c);
        adminCategory.ItemSelected += _ => FillItemList();
        adminItemFilter = new LineEdit { PlaceholderText = "filter...", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        adminItemFilter.TextChanged += _ => FillItemList();
        adminItemFilter.FocusEntered += () => { chatFocused = true; };
        adminItemFilter.FocusExited += () => { chatFocused = false; };
        top.AddChild(adminCategory); top.AddChild(adminItemFilter);
        adminItemList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single };
        adminItemList.ItemActivated += _ => GiveSelectedItem();
        var bottom = new HBoxContainer();
        adminTarget = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        adminCount = new SpinBox { MinValue = 1, MaxValue = 100000, Value = 1, CustomMinimumSize = new Vector2(90, 0), TooltipText = "amount (stackable items: reagents, gold, food...) or copies (single items, max 20)" };
        var give = new Button { Text = "Give" };
        give.Pressed += GiveSelectedItem;
        bottom.AddChild(new Label { Text = "To:" }); bottom.AddChild(adminTarget); bottom.AddChild(adminCount); bottom.AddChild(give);
        box.AddChild(top); box.AddChild(adminItemList); box.AddChild(bottom);
        if (adminItems.Count == 0) box.AddChild(new Label { Text = "items.json not found (run tools/make_itemlist.py)" });
        FillItemList();
        return box;
    }

    void FillItemList()
    {
        if (adminItemList == null) return;
        string cat = adminCategory.Selected > 0 ? adminCategory.GetItemText(adminCategory.Selected) : null;
        string f = (adminItemFilter?.Text ?? "").Trim();
        adminItemList.Clear(); adminItemsShown.Clear();
        foreach (var i in adminItems)
        {
            if (cat != null && i.Category != cat) continue;
            if (f != "" && i.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && i.Class.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
            adminItemList.AddItem(cat == null ? $"{i.Name}   [{i.Category}]" : i.Name);
            adminItemsShown.Add(i);
        }
    }

    /// <summary>Targets = ourselves + the other players in this room (offline players cannot be given items).</summary>
    void RefreshAdminTargets()
    {
        if (adminTarget == null) return;
        uint keep = adminTarget.Selected >= 0 && adminTarget.Selected < adminTargetIds.Count ? adminTargetIds[adminTarget.Selected] : client.PlayerId;
        adminTarget.Clear(); adminTargetIds.Clear();
        adminTarget.AddItem("Myself (" + client.CharacterName + ")"); adminTargetIds.Add(client.PlayerId);
        foreach (var o in client.Objects)
            if (o.Flags.IsPlayer && o.ID != client.PlayerId) { adminTarget.AddItem(client.Resolve(o.NameRID) ?? ("#" + o.ID)); adminTargetIds.Add(o.ID); }
        int idx = adminTargetIds.IndexOf(keep); adminTarget.Selected = idx >= 0 ? idx : 0;
    }

    void GiveSelectedItem()
    {
        var sel = adminItemList.GetSelectedItems();
        if (sel.Length == 0) { AddChatLine("Select an item first.", "System"); return; }
        if (adminTargetIds.Count == 0) RefreshAdminTargets();
        var item = adminItemsShown[sel[0]];
        uint target = adminTargetIds[Math.Max(0, adminTarget.Selected)];
        int n = (int)adminCount.Value;
        AddChatLine($"Giving {(item.Stack ? n + " x " : "")}{item.Name} to {adminTarget.GetItemText(adminTarget.Selected)} ...", "System");
        if (item.Stack) { pendingGiveTargets.Enqueue(target); client.SendAdmin($"create object {item.Class} number int {n}"); }
        else for (int i = 0; i < Math.Min(n, 20); i++) { pendingGiveTargets.Enqueue(target); client.SendAdmin($"create object {item.Class}"); }
    }

    void FillAdminList()
    {
        if (adminRoomList == null) return;
        string f = (adminFilter?.Text ?? "").Trim();
        adminRoomList.Clear(); adminShown.Clear();
        foreach (var r in adminRooms)
        {
            if (f != "" && r.name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && r.rid.ToString() != f && !(r.file ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            adminRoomList.AddItem($"{r.name}   #{r.rid}");
            adminShown.Add(r.rid);
        }
    }

    void AdminTeleport(int rid)
    {
        string name = adminRooms.Find(r => r.rid == rid).name ?? "?";
        AddChatLine($"Teleporting to {name} (#{rid}) ...", "System");
        client.TeleportTo(rid);
    }

    /// <summary>/give &lt;item name or class&gt; [amount]: create the item and put it in our own inventory.</summary>
    void AdminGiveByName(string q)
    {
        int n = 1;
        var parts = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && int.TryParse(parts[^1], out int amt)) { n = Math.Max(1, amt); q = string.Join(" ", parts[..^1]); }
        var hits = adminItems.FindAll(i => i.Class.Equals(q, StringComparison.OrdinalIgnoreCase) || i.Name.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (hits.Count == 0) hits = adminItems.FindAll(i => i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || i.Class.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        if (hits.Count == 0) { AddChatLine($"No item matches '{q}'.", "System"); return; }
        if (hits.Count > 1) { AddChatLine($"{hits.Count} items match: " + string.Join(", ", hits.GetRange(0, Math.Min(6, hits.Count)).ConvertAll(i => i.Name)) + " ...", "System"); return; }
        var item = hits[0];
        AddChatLine($"Giving {(item.Stack ? n + " x " : "")}{item.Name} to yourself ...", "System");
        int copies = item.Stack ? 1 : Math.Min(n, 20);
        for (int i = 0; i < copies; i++)
        {
            pendingGiveTargets.Enqueue(client.PlayerId);
            client.SendAdmin(item.Stack ? $"create object {item.Class} number int {n}" : $"create object {item.Class}");
        }
    }

    void AdminGoByName(string q)
    {
        var hits = adminRooms.FindAll(r => r.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || r.rid.ToString() == q);
        if (hits.Count == 0) AddChatLine($"No room matches '{q}'.", "System");
        else if (hits.Count == 1 || hits[0].name.Equals(q, StringComparison.OrdinalIgnoreCase)) AdminTeleport(hits[0].rid);
        else AddChatLine($"{hits.Count} rooms match: " + string.Join(", ", hits.GetRange(0, Math.Min(6, hits.Count)).ConvertAll(r => r.name)) + " ...", "System");
    }

    /// <summary>/where [note] or F9: short position line in chat; fuller report to the clipboard and userdata/where.txt (appended) for bug reports.</summary>
    void WhereReport(string note)
    {
        if (walker == null || client.Room == null || currentRoom == null) { AddChatLine("Not in a room yet.", "System"); return; }
        string file = Path.GetFileName(ResolveFile(client.Resolve(client.Room.RoomFileRID)) ?? "?");
        string room = client.Resolve(client.Room.RoomNameRID) ?? "?";
        ushort kx = GameClient.WorldToKod(walker.X), ky = GameClient.WorldToKod(walker.Y);
        int ang = (int)((((-Mathf.RadToDeg(yaw) - 90f) / 360f * 4096f) % 4096f + 4096f) % 4096f);
        int sn = currentRoom.FindSector(walker.X, walker.Y);
        string sector = sn > 0 ? $"sector {sn} floor {currentRoom.Sectors[sn - 1].FloorHeight} ceil {currentRoom.Sectors[sn - 1].CeilingHeight} flags 0x{currentRoom.Sectors[sn - 1].Flags:X}" : "outside sectors";
        string tgt = targetId == 0 ? "none" : $"{client.Resolve(client.Objects.Find(o => o.ID == targetId)?.NameRID ?? 0) ?? "?"} (#{targetId})";
        var near = new List<string>();
        foreach (var o in client.Objects)
        {
            if (o.ID == client.PlayerId || !objPos.TryGetValue(o.ID, out var p)) continue;
            float d = Mathf.Sqrt((p.x - walker.X) * (p.x - walker.X) + (p.y - walker.Y) * (p.y - walker.Y)) / 1024f;
            if (d <= 8f) near.Add($"{client.Resolve(o.NameRID) ?? "?"} #{o.ID} {d:F1}sq");
        }
        near.Sort(StringComparer.Ordinal);
        string line = $"{file} x={kx} y={ky} a={ang}" + (note != "" ? "  # " + note : "");
        string report = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n" +
                        $"  room: {room}  char: {client.CharacterName}  hp: {client.GetNumeric(Meridian59.Common.Enums.StatGroup.Condition, 1)?.ValueCurrent}\n" +
                        $"  world: {walker.X:F0},{walker.Y:F0},z={walker.Z:F0}  grid: {kx / 64.0:F1},{ky / 64.0:F1}  {sector}\n" +
                        $"  target: {tgt}  resting: {resting}  lead: {LeadSeconds}\n" +
                        $"  nearby (<=8 sq): {(near.Count > 0 ? string.Join("; ", near.GetRange(0, Math.Min(near.Count, 12))) : "none")}\n";
        AddChatLine("WHERE: " + line + "   (full report copied)", "System");
        GD.Print("[where] " + line);
        DisplayServer.ClipboardSet(report);
        try { File.AppendAllText(Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/where.txt")), report + "\n"); } catch { }
    }

    CanvasLayer mapMaxLayer; Control mapMaxHost; VBoxContainer mapNormalBox;

    /// <summary>Corner arrow on the map: the same map control fills the screen (over everything but the HUD windows' tooltips) and goes back on the second click.</summary>
    void SetMapMaximized(bool on)
    {
        if (miniMap == null || miniMap.Maximized == on) return;
        if (mapMaxLayer == null)
        {
            mapMaxLayer = new CanvasLayer { Layer = 8 };
            mapMaxHost = new PanelContainer { Visible = false };
            mapMaxHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            mapMaxHost.OffsetLeft = 30; mapMaxHost.OffsetTop = 30; mapMaxHost.OffsetRight = -30; mapMaxHost.OffsetBottom = -30;
            mapMaxLayer.AddChild(mapMaxHost); AddChild(mapMaxLayer);
            mapNormalBox = (VBoxContainer)miniMap.GetParent();
        }
        miniMap.Maximized = on;
        miniMap.GetParent().RemoveChild(miniMap);
        if (on) { mapMaxHost.AddChild(miniMap); mapMaxHost.Visible = true; }
        else { mapMaxHost.Visible = false; mapNormalBox.AddChild(miniMap); }
        miniMap.QueueRedraw();
    }

    bool IsCompanion(uint id) => client.Group.Exists(m => m.Id == id && m.Kind == 2);

    /// <summary>The group list arrives after the object: give companions their green name + "&lt;Companion&gt;" tag.</summary>
    void RefreshCompanionLabels()
    {
        foreach (var m in client.Group)
        {
            if (m.Kind != 2 || !objSprites.TryGetValue(m.Id, out var sp) || !IsInstanceValid(sp)) continue;
            var o = client.Objects.Find(x => x.ID == m.Id);
            if (o == null) continue;
            var old = sp.GetNodeOrNull<Label3D>("name");
            if (old != null && old.Text.Contains("<Companion>")) continue;
            float h = old != null ? old.Position.Y - 0.2f : (sp.Texture != null ? sp.Texture.GetHeight() * sp.PixelSize : 1.6f);
            AddNameLabel(sp, o, h);
        }
    }

    /// <summary>Floating name above players (white), monsters (red) and NPCs / shopkeepers (yellow); furniture and items get none.</summary>
    void AddNameLabel(Sprite3D sp, RoomObject o, float spriteHeight)
    {
        var f = o.Flags;
        Color col;
        if (f.IsPlayer) col = new Color(0.85f, 0.92f, 1f);
        else if (f.IsAttackable) col = new Color(1f, 0.45f, 0.4f);
        else if (f.IsOfferable || f.IsBuyable) col = new Color(1f, 0.9f, 0.45f);
        else if (IsCompanion(o.ID)) col = new Color(0.45f, 1f, 0.5f);
        else return;
        if (IsCompanion(o.ID)) col = new Color(0.45f, 1f, 0.5f);
        string name = client.Resolve(o.NameRID);
        if (string.IsNullOrEmpty(name)) return;
        if (sp.GetNodeOrNull("name") is Node oldLbl) { sp.RemoveChild(oldLbl); oldLbl.QueueFree(); }   // detach at once so the new one keeps the name "name" (no double label)
        bool comp = IsCompanion(o.ID);
        var lblNode = new Label3D
        {
            Text = IsCompanion(o.ID) ? name + "\n<Companion>" : name,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            PixelSize = 0.006f,
            FontSize = 40,
            OutlineSize = 10,
            Modulate = col,
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            Position = new Vector3(0, spriteHeight + 0.2f, 0),
            Name = "name",
        };
        if (comp) { lblNode.VerticalAlignment = VerticalAlignment.Bottom; lblNode.SetMeta("lift", 0.12f); lblNode.Position = new Vector3(0, spriteHeight + 0.32f, 0); }   // two lines grow upwards, clear of the head
        sp.AddChild(lblNode);
    }

    /// <summary>Per frame: advance every object's animation (the server-sent cycle/once animations, using the movement animation while it walks) and
    /// show the frame for the current group and viewing angle (16 buckets of 256 angle units). Composed avatars are cached per (object, angle, frame).</summary>
    void UpdateAvatars()
    {
        if (avatars.Count == 0) return;
        double nowMs = Time.GetTicksMsec();
        int viewer = (int)Math.Round((-yaw - Mathf.Pi / 2f) / Mathf.Tau * 4096f);
        int composeBudget = 4;                                    // composing a frame costs a few ms; spread a crowd's updates over several frames
        foreach (var id in new List<uint>(avatars.Keys))
        {
            var st = avatars[id];
            if (!objSprites.TryGetValue(id, out var sp)) continue;
            var o = st.O;
            bool moving = id == client.PlayerId ? selfMoving : objTargets.ContainsKey(id);
            bool motion = moving && o.MotionAnimation != null;
            var anim = motion ? o.MotionAnimation : o.Animation;
            string path = ResolveFile(client.Resolve(o.OverlayFileRID));
            if (path == null) continue;
            if (anim != null && !(anim is Meridian59.Data.Models.AnimationNone)) { anim.GroupMax = sprites.Groups(path); anim.Tick(nowMs, 0); }
            int key = motion ? 1 : 0;
            key = key * 31 + AvatarComposer.GroupOf(anim);
            if (st.Avatar)
                foreach (var so in motion ? o.MotionSubOverlays : o.SubOverlays)
                {
                    var sa = so.Animation;
                    if (sa != null && !(sa is Meridian59.Data.Models.AnimationNone))
                    { string sp2 = ResolveFile(client.Resolve(so.ResourceID)); sa.GroupMax = sp2 != null ? sprites.Groups(sp2) : 0; sa.Tick(nowMs, 0); }
                    key = key * 31 + AvatarComposer.GroupOf(sa);
                }
            int objA = id == client.PlayerId ? ((viewer % 4096) + 4096) % 4096 : (objAngle.TryGetValue(id, out var a) ? a : o.AngleUnits);
            int bucket = AvatarComposer.RelativeAngle(objA, viewer) / 256;
            if (bucket == st.Bucket && key == st.Key) continue;
            if (AnimLog) GD.Print($"[anim] #{id} {client.Resolve(o.NameRID)} {(motion ? "move" : "normal")} {anim?.GetType().Name} group {AvatarComposer.GroupOf(anim)} bucket {bucket}{(st.Avatar ? " avatar" : "")}");

            if (st.Avatar)
            {
                if (!avatarCache.TryGetValue((id, bucket, key), out var res))
                {
                    if (composeBudget-- <= 0) continue;
                    res = AvatarComposer.Compose(o, (ushort)(bucket * 256 + 128), rid => ResolveFile(client.Resolve(rid)), motion, bucket >= 5 && bucket <= 10);
                    if (avatarCache.Count > 4000) avatarCache.Clear();
                    avatarCache[(id, bucket, key)] = res;
                    if (AnimDump != null && id == client.PlayerId && res != null) res.Texture.GetImage().SavePng($"{AnimDump}/self_{(motion ? "m" : "n")}_{bucket}_{avatarCache.Count}.png");
                }
                st.Bucket = bucket; st.Key = key;
                if (res == null) continue;
                sp.Texture = res.Texture; sp.PixelSize = res.PixelSize; sp.Offset = res.Offset;
                if (sp.GetNodeOrNull<Label3D>("name") is Label3D lbl) lbl.Position = new Vector3(0, res.Top + 0.2f + (float)lbl.GetMeta("lift", 0f), 0);
            }
            else
            {
                st.Bucket = bucket; st.Key = key;
                var e = sprites.Get(path, AvatarComposer.GroupOf(anim), (ushort)(bucket * 256 + 128), o.ColorTranslation);
                if (e == null) continue;
                float pix = 16f * RoomBuilder.S / e.Shrink;
                sp.Texture = e.Texture; sp.PixelSize = pix;
                sp.Offset = new Vector2(e.XOffset, e.Height / 2f - e.YOffset);
                if (sp.GetNodeOrNull<Label3D>("name") is Label3D lbl) lbl.Position = new Vector3(0, e.Height * pix + 0.2f + (float)lbl.GetMeta("lift", 0f), 0);
            }
        }
    }

    /// <summary>Animated textures from the room file (official roomanim.c): cycling groups (period 10000/speed ms) and scrolling water/walls.</summary>
    void UpdateRoomAnimations()
    {
        if (roomAnims.Count == 0) return;
        double t = Time.GetTicksMsec() - roomAnimStart;
        foreach (var a in roomAnims)
        {
            if (a.Cycle)
            {
                int g = a.Wall && !audioSettings.CycleWalls ? 0 : (int)(t / a.PeriodMs) % Math.Max(1, a.Groups);
                if (g == a.LastGroup) continue;
                a.LastGroup = g;
                if (AnimLog && roomAnimLogged++ < 12) GD.Print($"[roomanim] texture {a.Bmp} -> group {g} of {a.Groups} (period {a.PeriodMs:F0} ms)");
                var e = textures.Get(a.Bmp, a.Cutout, g);
                if (e != null) a.Mat.SetShaderParameter("tex", e.Texture);
            }
            else
            {
                if (!audioSettings.ScrollWater) { a.Mat.SetShaderParameter("uv_off", Vector2.Zero); continue; }
                double steps = t / a.PeriodMs;
                float xo = (float)(a.Dx * steps), yo = (float)(a.Dy * steps), u, v;
                if (a.Wall)
                {
                    float sgn = a.Backwards ? -1f : 1f;
                    u = sgn * xo * a.Shrink / a.TexH; v = -sgn * yo * a.Shrink / a.TexW;
                }
                else { u = -yo / 1024f; v = xo / 1024f; }   // floors / ceilings: one step = 1/1024 of a tile (official: offset in world units, texture coords scaled by 1/(64*PETER_FUDGE))
                a.Mat.SetShaderParameter("uv_off", new Vector2(u - Mathf.Floor(u), v - Mathf.Floor(v)));
            }
        }
    }

    void PlaceSprite(uint id)
    {
        if (!objSprites.TryGetValue(id, out var sp) || !objPos.TryGetValue(id, out var p)) return;
        int sn = currentRoom.FindSector(p.x, p.y);
        float floor = sn > 0 ? currentRoom.Sectors[sn - 1].FloorHeight * 16f - Walker.DepthOf(currentRoom.Sectors[sn - 1].Flags) : 0f;   // objects wade too (official: floor - sector depth)
        sp.Position = RoomBuilder.P(p.x, p.y, floor);
    }

    void UpdateWalkCamera()
    {
        var eye = RoomBuilder.P(walker.X, walker.Y, walker.Z + Walker.EyeHeight);
        cam.Position = eye;
        ApplyLook();
        if (!thirdPerson || currentRoom == null) return;

        // third person: move the camera back along the view direction, but stop short of walls (probe with a Walker, which also respects room edges)
        var back = cam.Basis.Z;                                   // the camera looks along -Z
        float totalX = back.X / RoomBuilder.S * camDist, totalY = back.Z / RoomBuilder.S * camDist;   // world fineness for the full distance
        float total = Mathf.Sqrt(totalX * totalX + totalY * totalY), t = 1f;
        var pr = new Walker(currentRoom) { X = walker.X, Y = walker.Y, Z = walker.Z };
        if (total > 1f)
        {
            const int n = 10;
            for (int i = 0; i < n; i++) if (!pr.Move(totalX / n, totalY / n)) break;
            float got = Mathf.Sqrt((pr.X - walker.X) * (pr.X - walker.X) + (pr.Y - walker.Y) * (pr.Y - walker.Y));
            t = Mathf.Clamp(got / total - 0.04f, 0.12f, 1f);
        }
        var pos = eye + back * camDist * t;
        float floorZ = pr.FloorAt(walker.X + totalX * t, walker.Y + totalY * t);
        if (!float.IsNaN(floorZ)) pos.Y = Mathf.Max(pos.Y, (floorZ + 256f) * RoomBuilder.S);   // keep the camera above the ground
        cam.Position = pos;
        // camera squeezed against a wall: hide our own avatar so we do not look out of the inside of its head
        if (objSprites.TryGetValue(client.PlayerId, out var me)) me.Visible = camDist * t > 1.3f;
    }

    void ToggleThirdPerson()
    {
        thirdPerson = !thirdPerson;
        if (objSprites.TryGetValue(client.PlayerId, out var me)) me.Visible = false;     // UpdateWalkCamera shows it again in third person
        if (walker != null) { SyncSelfSprite(); UpdateWalkCamera(); }
        AddChatLine(thirdPerson ? "Third-person camera (V to switch back, mouse wheel = distance)." : "First-person camera.", "System");
    }

    /// <summary>Our own avatar follows the locally predicted position.</summary>
    void SyncSelfSprite()
    {
        if (walker == null || !objSprites.ContainsKey(client.PlayerId)) return;
        objPos[client.PlayerId] = (walker.X, walker.Y);
        PlaceSprite(client.PlayerId);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && thirdPerson && onlineMode && roomShown && !chatFocused)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { camDist = Mathf.Clamp(camDist * 0.9f, 1f, 14f); if (walker != null) UpdateWalkCamera(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown) { camDist = Mathf.Clamp(camDist * 1.1f, 1f, 14f); if (walker != null) UpdateWalkCamera(); }
        }
    }

    // called every frame while online and in a room
    void WalkProcess(float dt)
    {
        if (resyncRest && roomShown) { resyncRest = false; if (resting) client.Rest(); else client.Stand(); }   // like the official InterfaceResetData: tell the server our rest state after a room change
        UpdateAvatars();
        PollTargetHealth();
        UpdateLifts((float)dt); UpdateSky(); UpdateLights(); UpdateSpriteLight();
        UpdateRoomAnimations();
        UpdateProjectiles(dt);
        if (LagLog && lagCount > 0 && Time.GetTicksMsec() / 1000.0 > lagNext)
        { GD.Print($"[lag] lead={LeadSeconds}s mean distance sprite->new report = {lagSum / lagCount:F2} squares over {lagCount} updates"); lagSum = 0; lagCount = 0; lagNext = Time.GetTicksMsec() / 1000.0 + 5; }
        // other objects glide toward their targets
        foreach (var id in new List<uint>(objTargets.Keys))
        {
            var t = objTargets[id];
            if (!objPos.TryGetValue(id, out var p)) { objTargets.Remove(id); continue; }
            float dx = t.x - p.x, dy = t.y - p.y, d = Mathf.Sqrt(dx * dx + dy * dy);
            int backlog = objQueue.TryGetValue(id, out var oq) ? oq.Count : 0;
            float step = t.speed * dt * (d > 2048f ? 1.6f : 1f) * (1f + 0.3f * Mathf.Min(backlog, 3));      // behind: catch up faster
            if (d <= step)
            {
                objPos[id] = (t.x, t.y);
                if (backlog > 0) { var nx0 = oq[0]; oq.RemoveAt(0); objTargets[id] = (nx0.x, nx0.y, t.speed); }
                else objTargets.Remove(id);
            }
            else
            {
                float nx = p.x + dx / d * step, ny = p.y + dy / d * step;
                // glide along walls instead of cutting through a wall corner: the walker slides along blocking walls like the player does
                if (currentRoom != null && d < 4096f)
                {
                    objWalker ??= new Walker(currentRoom);
                    if (objWalkerRoom != currentRoom) { objWalker = new Walker(currentRoom); objWalkerRoom = currentRoom; }
                    if (objWalker.Place(p.x, p.y) && objWalker.Move(dx / d * step, dy / d * step))
                    {
                        float moved = Mathf.Sqrt((objWalker.X - p.x) * (objWalker.X - p.x) + (objWalker.Y - p.y) * (objWalker.Y - p.y));
                        if (moved > step * 0.3f) { nx = objWalker.X; ny = objWalker.Y; }          // otherwise fully blocked (door, odd geometry): go straight
                    }
                }
                DbgMoveDrawn(id, p.x, p.y, nx, ny);
                objPos[id] = (nx, ny);
            }
            PlaceSprite(id);
        }

        if (flyMode || walker == null || chatFocused) return;

        bool run = Input.IsKeyPressed(Key.Shift) && (client.GetNumeric(Meridian59.Common.Enums.StatGroup.Condition, 3)?.ValueCurrent ?? 100) >= MinVigorToRun;   // official MIN_VIGOR: too tired = walk
        float fwd = 0, side = 0;
        if (!turnDone && TurnAt > 0 && Time.GetTicksMsec() / 1000.0 >= TurnAt) { turnDone = true; yaw = Mathf.DegToRad(TurnYaw); }   // test hook M59_TURNAT / M59_TURNYAW: change direction mid-walk
        bool autoGo = autoWalk > 0 && Time.GetTicksMsec() / 1000.0 >= AutoWalkStart;   // test hook M59_WALKSTART=<seconds since the client started>
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) || autoGo) fwd += 1;
        if (autoGo) { autoWalk -= dt; if (autoWalk <= 0) GD.Print($"autowalk done at world ({walker.X:F0},{walker.Y:F0}) z={walker.Z:F0}"); }
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) fwd -= 1;
        if (Input.IsKeyPressed(Key.D)) side += 1;
        if (Input.IsKeyPressed(Key.A)) side -= 1;
        if (Input.IsKeyPressed(Key.Left)) yaw += 1.8f * dt;
        if (Input.IsKeyPressed(Key.Right)) yaw -= 1.8f * dt;
        if (resting || fxParalyzed) { fwd = 0; side = 0; }          // sitting: you can look around but not walk (press R to stand)
        selfMoving = false;

        if (fwd != 0 || side != 0)
        {
            float sp = Walker.WalkSpeed * (run ? 2f : 1f) * walker.WadeFactor() * dt;
            // world directions from yaw: forward = (-sin, cos), right = (cos, sin)
            float fx = -Mathf.Sin(yaw), fy = -Mathf.Cos(yaw), rx = Mathf.Cos(yaw), ry = -Mathf.Sin(yaw);
            float mx = fx * fwd + rx * side, my = fy * fwd + ry * side;
            float ml = Mathf.Sqrt(mx * mx + my * my);
            if (walker.Move(mx / ml * sp, my / ml * sp)) { movedSinceSend = true; lastRun = run; selfMoving = true; }   // blocked by a wall = not walking (no stride animation)
        }
        walker.UpdateZ(dt);
        UpdateWalkCamera();
        if (thirdPerson) SyncSelfSprite();

        // tell the server at most once per second (official MOVE_INTERVAL 1000 ms; faster trips the server's speedhack alert)
        double now = Time.GetTicksMsec() / 1000.0;
        float sdx = walker.X - lastSentX, sdy = walker.Y - lastSentY;
        bool far = sdx * sdx + sdy * sdy >= 256f * 256f;
        if (movedSinceSend && (far && now - lastSendTime > 1.0 || (fwd == 0 && side == 0 && now - lastSendTime > 1.0 && sdx * sdx + sdy * sdy > 16 * 16)))
        {
            client.SendMove(walker.X, walker.Y, lastRun);
            lastSentX = walker.X; lastSentY = walker.Y; lastSendTime = now; movedSinceSend = false;
        }
        ushort ang = (ushort)(((int)Math.Round((-yaw - Mathf.Pi / 2f) / Mathf.Tau * 4096f) % 4096 + 4096) % 4096);
        if (ang != lastSentAngle && now - lastTurnSendTime > 0.25)
        {
            client.SendTurn(ang);
            lastSentAngle = ang; lastTurnSendTime = now;
        }
    }

    // ------------------------------------------------------------------ chat
    void BuildHud()
    {
        string layoutPath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/hud_layout.json"));
        hudMgr = new HudManager(this, GetWindow(), layoutPath);
        BuildAudioSliders();
        LoadDebugSettings(); BuildDebugLogUi();
        BuildTopBarButtons();
        if (optScale > 0) hudMgr.SetScale(optScale);

        // --- chat window: log with the input line always visible below it
        var box = new VBoxContainer();
        chatLog = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.Click, SelectionEnabled = true, ContextMenuEnabled = true };   // drag to select, Ctrl+C or right-click to copy
        chatInput = new LineEdit { PlaceholderText = "Press Enter to chat  (/y yell, /e emote)", FocusMode = Control.FocusModeEnum.Click };
        chatInput.TextSubmitted += text =>
        {
            DbgMark(text);   // debug log: whatever is typed becomes a marker line (see DebugLog.cs)
            if (text == "/where" || text.StartsWith("/where ")) WhereReport(text.Length > 7 ? text.Substring(7).Trim() : "");
            else if (text.StartsWith("/go ") && client.IsAdmin) AdminGoByName(text.Substring(4).Trim());
            else if (text.StartsWith("/give ") && client.IsAdmin) AdminGiveByName(text.Substring(6).Trim());
            else if (text.StartsWith("/give ")) AddChatLine("/give is for admin accounts.", "System");
            else if (text.StartsWith("/go ")) AddChatLine("/go is for admin accounts.", "System");
            else if (!string.IsNullOrWhiteSpace(text)) client.SendSay(text);
            CloseChatInput(true);
        };
        chatInput.FocusEntered += () => { chatFocused = true; };
        chatInput.FocusExited += () => { chatFocused = false; };
        box.AddChild(chatLog); box.AddChild(chatInput);
        chatPanel = hudMgr.Add("chat", "Chat", box, new Vector2(0.01f, -0.02f), new Vector2(560, 220));

        // --- character window: face portrait on the left, Health / Mana / Vigor bars on the right (the name is the title)
        var cbox = new HBoxContainer();
        cbox.AddThemeConstantOverride("separation", 8);
        var faceHolder = new PanelContainer { CustomMinimumSize = new Vector2(78, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        faceHolder.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.11f, 0.95f), BorderColor = new Color(0.35f, 0.35f, 0.45f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        facePic = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        faceMissing = new Label { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        faceMissing.AddThemeFontSizeOverride("font_size", 32);
        faceMissing.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.25f));
        faceHolder.GuiInput += ev =>
        {   // click your own portrait to target yourself (for heals and buffs)
            if (ev is InputEventMouseButton fm && fm.Pressed && fm.ButtonIndex == MouseButton.Left)
            { targetId = client.PlayerId; AddChatLine("Target: yourself", "System"); }
        };
        faceHolder.AddChild(facePic); faceHolder.AddChild(faceMissing);
        var bars = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        bars.AddThemeConstantOverride("separation", 4);
        bars.AddChild(MakeBar("heal.bgf", new Color(0.78f, 0.16f, 0.16f), out barHealth, out txtHealth));
        bars.AddChild(MakeBar("ankh.bgf", new Color(0.20f, 0.38f, 0.90f), out barMana, out txtMana));
        bars.AddChild(MakeBar("bolticon.bgf", new Color(0.85f, 0.72f, 0.15f), out barVigor, out txtVigor));
        cbox.AddChild(faceHolder); cbox.AddChild(bars);
        statusWin = hudMgr.Add("status", client.CharacterName, cbox, new Vector2(0.01f, 0.12f), new Vector2(300, 118));

        // --- target window: portrait + name of whatever you have selected (click it, or Tab to cycle). Health is not sent by the vanilla server for others.
        var tbox = new HBoxContainer();
        tbox.AddThemeConstantOverride("separation", 8);
        var tHolder = new PanelContainer { CustomMinimumSize = new Vector2(64, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        tHolder.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.11f, 0.95f), BorderColor = new Color(0.35f, 0.35f, 0.45f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        targetPic = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        targetMissing = new Label { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        targetMissing.AddThemeFontSizeOverride("font_size", 28);
        targetMissing.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.25f));
        tHolder.AddChild(targetPic); tHolder.AddChild(targetMissing);
        var tText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        targetName = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        targetName.AddThemeFontSizeOverride("font_size", 17);
        targetInfo = new Label { Modulate = new Color(1, 1, 1, 0.6f) };
        tText.AddChild(targetName); tText.AddChild(targetInfo);
        targetHpBar = MakeMiniBar(1, 1, new Color(0.8f, 0.15f, 0.15f)); targetHpBar.Visible = false; targetHpBar.CustomMinimumSize = new Vector2(0, 12);
        tText.AddChild(targetHpBar);
        tbox.AddChild(tHolder); tbox.AddChild(tText);
        targetWin = hudMgr.Add("target", "Target", tbox, new Vector2(0.30f, 0.30f), new Vector2(250, 90), visibleByDefault: false);
        targetWin.Visible = false;

        // --- map window: the room name sits on top of the (future) minimap
        var mbox = new VBoxContainer();
        mbox.AddThemeConstantOverride("separation", 4);
        mapRoomLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        mapRoomLabel.AddThemeFontSizeOverride("font_size", 15);
        miniMap = new MiniMap { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        miniMap.PlayerX = () => walker?.X ?? 0; miniMap.PlayerY = () => walker?.Y ?? 0;
        miniMap.Facing = () => -yaw - Mathf.Pi / 2f;      // kod angle (radians): 0 = east, grows clockwise seen from above (y = south)
        miniMap.Objects = () => MapObjects();
        miniMap.TooltipText = "Mouse wheel: zoom.  Click: add / edit a note.  Corner arrow: full-screen map.";
        miniMap.ToggleMax = SetMapMaximized;
        var mapArea = miniMap;
        mbox.AddChild(mapRoomLabel); mbox.AddChild(mapArea);
        hudMgr.Add("map", "Map", mbox, new Vector2(-0.01f, 0.06f), new Vector2(220, 220));
        client.StatsChanged += UpdateBars;
        UpdateBars();

        // --- inventory window: grid of item slots; equipped items are highlighted (Meridian has no paperdoll)
        var ibox = new VBoxContainer();
        ibox.AddThemeConstantOverride("separation", 4);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        invGrid = new GridContainer { Columns = 4 };
        invGrid.AddThemeConstantOverride("h_separation", 4);
        invGrid.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(invGrid);
        invInfo = new Label { Text = "Double-click or Space: use / unuse.  Right-click: look.  Drag out to drop.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        invInfo.AddThemeFontSizeOverride("font_size", 12);
        ibox.AddChild(scroll); ibox.AddChild(invInfo);
        // inventory, spells and skills share one window as tabs (spells/skills tabs are added by BuildSpellWindows)
        ibox.Name = "Inventory";
        packTabs = new TabContainer();
        packTabs.AddChild(ibox);
        hudMgr.Add("inventory", "Inventory", packTabs, new Vector2(0.54f, 0.40f), new Vector2(300, 340));   // leaves the bottom-right corner free for the first-person weapon
        // --- look window (right-click an inventory item, or click anything in the world): picture top-left, name and description to its right
        var hrow = new HBoxContainer();
        hrow.AddThemeConstantOverride("separation", 10);
        itemPic = new TextureRect { CustomMinimumSize = new Vector2(84, 84), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        var rvbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rvbox.AddThemeConstantOverride("separation", 4);
        itemName = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 0) };
        itemName.AddThemeFontSizeOverride("font_size", 16);
        // the description scrolls (long NPC / item texts), in a smaller font
        itemDesc = new RichTextLabel { ScrollActive = true, FitContent = false, SelectionEnabled = true, ContextMenuEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 70) };
        itemDesc.AddThemeFontSizeOverride("normal_font_size", 13);
        // buttons for what can be done with the thing looked at (buy, sell, offer ...)
        lookActions = new HBoxContainer { Visible = false }; lookActions.AddThemeConstantOverride("separation", 6);
        rvbox.AddChild(itemName); rvbox.AddChild(itemDesc); rvbox.AddChild(lookActions);
        hrow.AddChild(itemPic); hrow.AddChild(rvbox);
        itemWin = hudMgr.Add("item", "Look", hrow, new Vector2(0.34f, 0.12f), new Vector2(430, 230), visibleByDefault: false);
        BuildHotbar();
        BuildGroupUi();
        BuildTradeUi();
        BuildSpellWindows();
        BuildAdminWindow();
        itemWin.AddCloseButton();
        client.LookReceived += OnLookReceived;
        client.PlayerOverlayReceived += OnPlayerOverlay;
        client.InventoryChanged += RebuildInventory;
        client.InventoryChanged += RefreshHotbar;
        RebuildInventory();

        // dropping an item on the 3D view drops it on the floor
        var dropLayer = new CanvasLayer { Layer = 1 };
        var dropTarget = new WorldDropTarget();
        dropTarget.ItemDropped += (id, count) => { client.DropItem(id, count); };
        dropLayer.AddChild(dropTarget);
        AddChild(dropLayer);

        AddChatLine("Press Enter to chat. Menu (top right, or F10) = window lock, UI scale, reset layout.", "System");
    }

    void AddChatLine(string text, string kind)
    {
        if (chatLog == null) { return; }
        string color = kind switch
        {
            "Yell" => "#ff7070", "Emote" => "#e0c060", "Group" => "#80d0ff", "Everyone" => "#a0ff80",
            "System" => "#b0b0b0", "Server" => "#c0a0ff", _ => "#ffffff",
        };
        string safe = (text ?? "").Replace("[", "[lb]");
        chatLog.AppendText($"[color={color}]{safe}[/color]\n");
        GD.Print($"[chat/{kind}] {text}");
    }

    void OpenChatInput()
    {
        chatFocused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        chatInput.Text = "";
        chatInput.GrabFocus();
    }

    void CloseChatInput(bool recapture)
    {
        chatFocused = false;
        chatInput.ReleaseFocus();
        chatInput.Text = "";

    }

    // test hook: type `autoSay` through the real Godot input pipeline (Enter, characters, Enter)
    void DriveTypingTest()
    {
        sayFrame++;
        if (sayFrame < 60) return;                       // let the room settle
        void key(Key code, char ch = '\0', bool pressed = true)
        {
            var ev = new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = pressed, Unicode = ch };
            Input.ParseInputEvent(ev);
        }
        if (sayStage == 0) { key(Key.Enter); key(Key.Enter, '\0', false); sayStage = 1; sayFrame = 60; GD.Print($"[test] pressed Enter, chatFocused={chatFocused}"); }
        else if (sayStage == 1 && sayFrame > 66)
        {
            foreach (char c in autoSay) { key((Key)char.ToUpperInvariant(c), c); key((Key)char.ToUpperInvariant(c), c, false); }
            GD.Print($"[test] typed, input text='{chatInput.Text}'");
            sayStage = 2; sayFrame = 60;
        }
        else if (sayStage == 2 && sayFrame > 66) { key(Key.Enter); key(Key.Enter, '\0', false); sayStage = 3; GD.Print($"[test] pressed Enter again, chatFocused={chatFocused}"); }
    }

    bool OverHudWindow()
    {
        if (hudMgr == null) return false;
        var m = GetViewport().GetMousePosition();
        foreach (var c in hudMgr.Root.GetChildren())
            if (c is HudWindow w && w.Visible && w.GetGlobalRect().HasPoint(m)) return true;
        return hudMgr.OverBar(m);
    }

    // ---- hotbar: number keys 1..0. A slot holds an item, spell, skill or the Attack button; drag from the
    //      Inventory / Spells / Skills windows onto a slot, right-click a slot to clear it. Saved per character.
    class HotEntry { public string Kind { get; set; } = ""; public uint Id { get; set; } public string Name { get; set; } = ""; }
    readonly HotEntry[] hotbar = new HotEntry[10];
    InvSlot[] hotSlots;

    string HotbarPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/hotbar_" + (client.CharacterName ?? "x").Replace('/', '_') + ".json"));

    void BuildHotbar()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        hotSlots = new InvSlot[10];
        for (int i = 0; i < 10; i++)
        {
            int slot = i;
            var b = new InvSlot { AcceptDrops = true, Hotkey = ((i + 1) % 10).ToString() };
            b.Activated = _ => ActivateHotbar(slot);
            b.Clicked = _ => ActivateHotbar(slot);
            b.RightClicked = _ => { hotbar[slot] = null; SaveHotbar(); RefreshHotbar(); };
            b.Dropped = (_, d) => AssignHotbar(slot, d);
            hotSlots[i] = b; row.AddChild(b);
        }
        var win = hudMgr.Add("hotbar", "Hotbar", row, new Vector2(0.28f, 0.10f), new Vector2(540, 88));
        win.Resizable = false;
        LoadHotbar();
        RefreshHotbar();
    }

    void AssignHotbar(int slot, Godot.Collections.Dictionary d)
    {
        string kind = (string)d["kind"];
        hotbar[slot] = new HotEntry { Kind = kind, Id = d.ContainsKey("id") ? (uint)(long)d["id"] : 0, Name = d.ContainsKey("name") ? (string)d["name"] : "" };
        SaveHotbar(); RefreshHotbar();
    }

    void SaveHotbar()
    {
        try { File.WriteAllText(HotbarPath, System.Text.Json.JsonSerializer.Serialize(hotbar)); } catch { }
    }

    void LoadHotbar()
    {
        for (int i = 0; i < 10; i++) hotbar[i] = null;
        bool loaded = false;
        try
        {
            if (File.Exists(HotbarPath))
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<HotEntry[]>(File.ReadAllText(HotbarPath));
                for (int i = 0; i < 10 && arr != null && i < arr.Length; i++) hotbar[i] = arr[i];
                loaded = true;
            }
        }
        catch { }
        if (!loaded) hotbar[0] = new HotEntry { Kind = "attack", Name = "Attack" };   // new characters start with Attack on 1
    }

    Texture2D IconFor(ObjectBase o)
    {
        var path = ResolveFile(client.Resolve(o.OverlayFileRID));
        ushort group = (o.Animation is AnimationNone an) ? an.Group : (ushort)1;
        if (group == 0) group = 1;
        return path != null ? sprites.Get(path, group, 0, o.ColorTranslation)?.Texture : null;
    }

    /// <summary>Redraw the hotbar from its entries (call again when spells / inventory / skills change).</summary>
    void RefreshHotbar()
    {
        if (hotSlots == null) return;
        for (int i = 0; i < 10; i++)
        {
            var h = hotbar[i]; var sl = hotSlots[i];
            sl.Icon = null; sl.Label = ""; sl.ItemId = 0; sl.Kind = "item"; sl.Count = 0; sl.TooltipText = "";
            if (h != null)
            {
                sl.Kind = h.Kind; sl.ItemId = h.Id; sl.TooltipText = h.Name;
                ObjectBase src = h.Kind == "spell" ? client.Spells.Find(x => x.ID == h.Id)
                               : h.Kind == "skill" ? client.Skills.Find(x => x.ID == h.Id)
                               : h.Kind == "item" ? client.Inventory.Find(x => x.ID == h.Id) : null;
                if (h.Kind == "attack") sl.Label = "Attack";
                else if (h.Kind.StartsWith("comp_")) { sl.Label = CommandLabel(h.Kind); sl.TooltipText = CommandTip(h.Kind); }
                else if (src != null) { sl.Icon = IconFor(src); sl.TooltipText = client.Resolve(src.NameRID) ?? h.Name; if (sl.Icon == null) sl.Label = h.Name; }
                else sl.Label = h.Name;       // not (yet) known, e.g. before the lists arrive
            }
            sl.QueueRedraw();
        }
    }

    /// <summary>Buttons that are not server spells / skills / items but can still be dragged to the hotbar.</summary>
    public static bool IsCommandKind(string k) => k == "attack" || (k != null && k.StartsWith("comp_"));
    static string CommandLabel(string k) => k switch { "comp_attack" => "Bots Atk", "comp_passive" => "Bots Stay", "comp_assist" => "Bots Aid", _ => k };
    static string CommandTip(string k) => k switch
    {
        "comp_attack" => "Companions attack your target (or what you are fighting)",
        "comp_passive" => "Companions stop fighting and only follow you (healers still heal and buff)",
        "comp_assist" => "Companions fight alongside you again",
        _ => k
    };

    void CompanionCommand(string kind)
    {
        if (!client.Group.Exists(m => m.Kind == 2)) { AddChatLine("You have no companions.", "System"); return; }
        switch (kind)
        {
            case "comp_attack":
                if (targetId == 0 || targetId == client.PlayerId) AddChatLine("Companions attack what you are fighting (no target selected).", "System");
                client.CompanionAttack(targetId == client.PlayerId ? 0 : targetId); break;
            case "comp_passive": client.CompanionPassive(); AddChatLine("Companions: passive.", "System"); break;
            case "comp_assist": client.CompanionAssist(); AddChatLine("Companions: assisting.", "System"); break;
        }
    }

    void ActivateHotbar(int slot)
    {
        if (slot < 0 || slot > 9 || hotbar[slot] == null) return;
        var h = hotbar[slot];
        switch (h.Kind)
        {
            case "attack": Attack(); break;
            case "comp_attack": case "comp_passive": case "comp_assist": CompanionCommand(h.Kind); break;
            case "spell": { var sp = client.Spells.Find(x => x.ID == h.Id); if (sp != null) CastSpell(sp); break; }
            case "item": if (client.Inventory.Exists(x => x.ID == h.Id)) ToggleUse(h.Id); break;
            case "skill": AddChatLine("Skills work automatically when you use them.", "System"); break;
        }
    }

    // ---- first-person hands / weapon (BP_PLAYER_OVERLAY): drawn in a screen corner behind the HUD windows, animated like the original
    class PovSlot { public int Hotspot; public uint Rid; public string File; public bool Once; public double Tick, Period; public int Group, Low, High, Final; public TextureRect View; public bool LogOnce = true; public byte Xlat; }
    readonly PovSlot[] pov = new PovSlot[2];
    Control povLayer;
    float PovScale = 1.8f;   // original bitmaps are drawn 1:1 in a ~640 px wide view; our view is bigger

    void OnPlayerOverlay(PlayerOverlay o)
    {
        int slot = (int)o.ID - 1;
        if (slot < 0 || slot > 1) return;
        var p = pov[slot] ??= new PovSlot();
        p.Hotspot = (int)o.RenderPosition;
        p.Xlat = o.ColorTranslation; p.Rid = o.OverlayFileRID; p.File = ResolveFile(client.Resolve(o.OverlayFileRID));
        p.Once = false; p.Period = 0;
        if (o.Animation is AnimationOnce ao) { p.Once = true; p.Period = ao.Period; p.Low = ao.GroupLow; p.High = ao.GroupHigh; p.Final = ao.GroupFinal; p.Group = ao.GroupLow; p.Tick = ao.Period; }
        else if (o.Animation is AnimationNone an) p.Group = an.Group;
        else p.Group = 1;
        GD.Print($"[pov] slot {slot} hotspot={p.Hotspot} file={client.Resolve(o.OverlayFileRID)} anim={(p.Once ? $"once {p.Period}ms {p.Low}..{p.High} -> {p.Final}" : "none group " + p.Group)}");
        if (p.View == null)
        {
            p.View = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore };
            povLayer.AddChild(p.View);
        }
    }

    void UpdatePov(double dt)
    {
        if (povLayer == null) return;
        povLayer.Visible = roomShown && !thirdPerson;
        var area = povLayer.GetViewportRect().Size;
        PovScale = Mathf.Max(1f, area.X / 640f);
        foreach (var p in pov)
        {
            if (p == null || p.View == null) continue;
            if (p.Once)
            {
                p.Tick -= dt * 1000.0;
                if (p.Tick <= 0)
                {
                    if (p.Group >= p.High) { p.Once = false; p.Group = p.Final; }
                    else p.Group++;
                    p.Tick = p.Period;
                }
            }
            var e = (p.Hotspot >= 1 && p.Hotspot <= 9 && p.Group >= 1 && p.File != null) ? sprites.Get(p.File, p.Group, 0, p.Xlat) : null;   // group 0 = finished -> hidden
            p.View.Visible = e != null;
            if (e == null) continue;
            float w = e.Width * PovScale, h = e.Height * PovScale;
            if (p.LogOnce) { p.LogOnce = false; GD.Print($"[pov] group {p.Group} bitmap {e.Width}x{e.Height} offset {e.XOffset},{e.YOffset} shrink {e.Shrink} area {area}"); }
            float x = p.Hotspot switch { 1 or 7 or 8 => 0, 5 or 4 or 3 => area.X - w, _ => (area.X - w) / 2 };
            float y = p.Hotspot switch { 1 or 2 or 3 => 0, 7 or 6 or 5 => area.Y - h, _ => (area.Y - h / 2) };
            p.View.Texture = e.Texture;
            p.View.Position = new Vector2(x + e.XOffset * PovScale, y + e.YOffset * PovScale);
            p.View.Size = new Vector2(w, h);
        }
    }

    // ---- spells and skills windows
    VBoxContainer skillFlow, spellBox; TabContainer packTabs;

    /// <summary>I / B / K: show the pack window on that tab, or hide it if that tab is already showing.</summary>
    void ToggleTab(int tab)
    {
        var w = hudMgr.Get("inventory");
        if (w == null) return;
        if (w.Visible && packTabs.CurrentTab == tab) { hudMgr.Toggle("inventory"); return; }
        packTabs.CurrentTab = tab;
        if (!w.Visible) hudMgr.Toggle("inventory");
    }
    static readonly string[] SchoolNames = { "?", "Shal'ille", "Qor", "Kraanan", "Faren", "Riija", "Jala", "DM" };
    static readonly Color[] SchoolColors = {
        Colors.White, new Color(0.55f, 0.8f, 1f), new Color(0.75f, 0.55f, 0.95f), new Color(0.9f, 0.85f, 0.45f),
        new Color(1f, 0.55f, 0.35f), new Color(0.55f, 0.9f, 0.6f), new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.4f, 0.4f) };

    void BuildSpellWindows()
    {
        spellBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        spellBox.AddThemeConstantOverride("separation", 6);
        var sc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        sc.AddChild(spellBox);
        var sbox = new VBoxContainer();
        sc.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        sbox.AddChild(sc);
        sbox.AddChild(new Label { Text = "Double-click: cast.  Drag onto the hotbar.  Right-click: look.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) });
        sbox.Name = "Spells"; packTabs.AddChild(sbox);

        skillFlow = new VBoxContainer();
        skillFlow.AddThemeConstantOverride("separation", 4);
        var sc2 = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        skillFlow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sc2.AddChild(skillFlow);
        var kbox = new VBoxContainer();
        kbox.AddChild(sc2);
        kbox.AddChild(new Label { Text = "Drag onto the hotbar.  Right-click: look.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) });
        kbox.Name = "Skills"; packTabs.AddChild(kbox);
        client.SpellsChanged += () => { RebuildSpells(); RefreshHotbar(); };
        client.SkillsChanged += () => { RebuildSkills(); RefreshHotbar(); };
        RebuildSpells(); RebuildSkills();
    }

    InvSlot MakeIconSlot(ObjectBase o, string kind)
    {
        string name = client.Resolve(o.NameRID) ?? "?";
        var slot = new InvSlot { ItemId = o.ID, ItemName = name, Icon = IconFor(o), Kind = kind, TooltipText = name, Label = name };
        slot.RightClicked = _ => client.LookAt(o.ID);
        return slot;
    }

    // descriptions of spells/skills for the hover text: fetched silently with a Look the first time a row is hovered
    readonly Dictionary<uint, string> lookDesc = new Dictionary<uint, string>();
    readonly HashSet<uint> silentLooks = new HashSet<uint>();
    readonly Dictionary<uint, ListRow> listRows = new Dictionary<uint, ListRow>();

    string RowTooltip(ListRow r, string extra)
    {
        string t = r.ItemName + (extra != "" ? "  (" + extra + ")" : "");
        return t + (lookDesc.TryGetValue(r.ItemId, out var d) && !string.IsNullOrWhiteSpace(d) ? "\n\n" + d : "\n\n(hover again in a moment for the description; right-click to look)");
    }

    ListRow MakeListRow(ObjectBase o, string kind, string extra, Color nameColor)
    {
        var row = new ListRow { ItemId = o.ID, ItemName = client.Resolve(o.NameRID) ?? "?", Icon = IconFor(o), Kind = kind, NameColor = nameColor };
        row.SetMeta("extra", extra);
        row.TooltipText = RowTooltip(row, extra);
        row.RightClicked = _ => client.LookAt(o.ID);
        row.Hovered = r =>
        {
            if (!lookDesc.ContainsKey(r.ItemId) && silentLooks.Add(r.ItemId)) client.LookAt(r.ItemId);
            r.TooltipText = RowTooltip(r, extra);
        };
        listRows[o.ID] = row;
        return row;
    }

    void RebuildSpells()
    {
        if (spellBox == null) return;
        foreach (var c in spellBox.GetChildren()) { spellBox.RemoveChild(c); c.QueueFree(); }
        listRows.Clear();
        if (client.Spells.Count == 0) { spellBox.AddChild(new Label { Text = "You do not know any spells yet." }); return; }
        foreach (var grp in client.Spells.GroupBy(x => (int)x.SchoolType).OrderBy(g => g.Key))
        {
            int sch = grp.Key >= 0 && grp.Key < SchoolNames.Length ? grp.Key : 0;
            var head = new Label { Text = SchoolNames[sch] };
            head.AddThemeColorOverride("font_color", SchoolColors[sch]);
            spellBox.AddChild(head);
            foreach (var sp in grp.OrderBy(s => client.Resolve(s.NameRID) ?? ""))
            { if (AnimLog && sch == 0) GD.Print($"[spell?] id {sp.ID} name {client.Resolve(sp.NameRID)}"); }
            foreach (var sp in grp.OrderBy(s => client.Resolve(s.NameRID) ?? ""))
            {
                var spell = sp;
                var row = MakeListRow(sp, "spell", SchoolNames[sch] + (sp.TargetsCount > 0 ? ", needs a target" : ""), Colors.White);
                row.Activated = _ => CastSpell(spell);
                spellBox.AddChild(row);
            }
        }
    }

    void RebuildSkills()
    {
        if (skillFlow == null) return;
        foreach (var c in skillFlow.GetChildren()) { skillFlow.RemoveChild(c); c.QueueFree(); }
        // the Attack button is not a server skill; it can be dragged to the hotbar from here
        skillFlow.AddChild(new ListRow { ItemName = "Attack", Kind = "attack", TooltipText = "Attack (F): hits your target, or the nearest thing in front of you.\nDrag onto the hotbar." });
        foreach (var sk in client.Skills) skillFlow.AddChild(MakeListRow(sk, "skill", "skill", Colors.White));
    }

    /// <summary>Cast a spell at the current target (or at once if it needs no target).</summary>
    void CastSpell(SpellObject sp)
    {
        string name = client.Resolve(sp.NameRID) ?? "spell";
        if (sp.TargetsCount == 0) { client.Cast(sp.ID); return; }
        bool valid = targetId != 0 && (targetId == client.PlayerId || client.Objects.Exists(o => o.ID == targetId));
        if (!valid) { AddChatLine($"{name} needs a target: click something first (click your portrait to target yourself).", "System"); return; }
        client.Cast(sp.ID, targetId);
    }

    bool TargetOnScreen(uint id)
    {
        if (cam == null || !objSprites.TryGetValue(id, out var sp) || !IsInstanceValid(sp)) return false;
        var p = sp.GlobalPosition;
        if (cam.IsPositionBehind(p)) return false;
        var vp = GetViewport().GetVisibleRect().Size;
        var q = cam.UnprojectPosition(p);
        return q.X >= 0 && q.X <= vp.X && q.Y >= 0 && q.Y <= vp.Y;
    }

    /// <summary>Attack the clicked target if it is still here, otherwise the nearest attackable thing in front of us.</summary>
    void Attack()
    {
        if (walker == null) return;
        uint id = 0;
        if (targetId != 0)
        {   // official UserAttackClosest: a selected target is attacked only if it is on screen (the D3D renderer's "visible object" test is just the view frustum, walls do not count)
            if (targetId == client.PlayerId) { AddChatLine("You can't attack yourself.", "System"); return; }
            if (!TargetOnScreen(targetId)) { AddChatLine("Your target is not visible.", "System"); return; }
            id = targetId;
        }
        else
        {
            float fx = -Mathf.Sin(yaw), fy = -Mathf.Cos(yaw), best = 8192f * 8192f;
            foreach (var o in client.Objects)
            {
                if (o.ID == client.PlayerId || !o.Flags.IsAttackable || !objPos.TryGetValue(o.ID, out var p)) continue;
                float dx = p.x - walker.X, dy = p.y - walker.Y, d2 = dx * dx + dy * dy;
                if (d2 < best && dx * fx + dy * fy > 0) { best = d2; id = o.ID; }
            }
        }
        if (id == 0) { AddChatLine("There is nothing to attack in front of you.", "System"); return; }
        client.Attack(id);
    }

    IEnumerable<(float x, float y, int kind)> MapObjects()
    {
        foreach (var o in client.Objects)
        {
            if (o.ID == client.PlayerId || !objPos.TryGetValue(o.ID, out var p)) continue;
            // like the original: only players and attackable things appear (no furniture / items); group companions are drawn green
            if (IsCompanion(o.ID)) yield return (p.x, p.y, 3);
            else if (o.Flags.IsPlayer) yield return (p.x, p.y, 2);
            else if (o.Flags.IsAttackable) yield return (p.x, p.y, 1);
        }
    }

    void UpdateTargetWindow()
    {
        if (targetWin == null) return;
        if (targetId != 0 && targetId != client.PlayerId && !client.Objects.Exists(o => o.ID == targetId)) targetId = 0;   // left the room / died
        if (targetId == 0) { if (targetWin.Visible) targetWin.Visible = false; targetShown = 0; targetDirty = false; return; }
        if (!targetDirty && targetShown == targetId) return;
        targetDirty = false; targetShown = targetId;
        var ro = targetId == client.PlayerId ? client.Objects.Find(o => o.ID == targetId) ?? client.Player : client.Objects.Find(o => o.ID == targetId);
        if (ro == null) { targetWin.Visible = false; return; }
        var f = ro.Flags;
        string kind = f.IsPlayer ? (ro.ID == client.PlayerId ? "You" : "Player") : f.IsAttackable ? "Creature" : "Non-player character";
        Color col = f.IsPlayer ? new Color(0.85f, 0.92f, 1f) : f.IsAttackable ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.9f, 0.4f);
        targetName.Text = client.Resolve(ro.NameRID) ?? "?";
        targetName.AddThemeColorOverride("font_color", col);
        targetInfo.Text = kind;
        Texture2D tex = null;
        if (f.IsPlayer) tex = FaceComposer.Compose(ro, rid => ResolveFile(client.Resolve(rid)));
        if (tex == null)
        {
            var path = ResolveFile(client.Resolve(ro.OverlayFileRID));
            ushort g = (ro.Animation is Meridian59.Data.Models.AnimationNone an) ? an.Group : (ushort)1;
            if (path != null) tex = sprites.Get(path, g == 0 ? 1 : g, 0, ro.ColorTranslation)?.Texture;
        }
        targetPic.Texture = tex; targetMissing.Visible = tex == null;
        if (!targetWin.Visible) targetWin.Visible = true;
    }

    /// <summary>Tab: select the next attackable creature or player nearby (nearest first, wraps around).</summary>
    void CycleTarget()
    {
        var list = new List<(float d, uint id)>();
        foreach (var o in client.Objects)
        {
            if (o.ID == client.PlayerId || !(o.Flags.IsAttackable || o.Flags.IsPlayer) || !objPos.TryGetValue(o.ID, out var p)) continue;
            float dx = p.x - (walker?.X ?? 0), dy = p.y - (walker?.Y ?? 0); float d = dx * dx + dy * dy;
            if (d < 30f * 1024 * 30f * 1024) list.Add((d, o.ID));
        }
        if (list.Count == 0) { targetId = 0; return; }
        list.Sort((a, b) => a.d.CompareTo(b.d));
        int i = list.FindIndex(t => t.id == targetId);
        targetId = list[(i + 1) % list.Count].id;
    }

    void UpdateStatusWindow()
    {
        UpdateTargetWindow();
        CheckAutoStand();
        if (client == null || client.Room == null) return;
        if (mapRoomLabel != null) mapRoomLabel.Text = client.Resolve(client.Room.RoomNameRID) ?? "?";
        if (statusWin != null && statusWin.TitleText != client.CharacterName) statusWin.SetTitle(client.CharacterName);
    }

    void UpdateFace()
    {
        var pl = client.Player;
        var tex = pl != null ? FaceComposer.Compose(pl, rid => ResolveFile(client.Resolve(rid))) : null;
        if (facePic != null) facePic.Texture = tex;
        if (faceMissing != null) faceMissing.Visible = tex == null;
    }

    // test hook: drag the chat window by its title bar using synthetic mouse events through the real input pipeline
    static Vector2 DragDelta => System.Environment.GetEnvironmentVariable("M59_DRAGDELTA") is string dd && dd.Split(',') is var pp && pp.Length == 2 ? new Vector2(float.Parse(pp[0]), float.Parse(pp[1])) : new Vector2(300, -200);
    void DriveDragTest()
    {
        dragFrame++;
        var w = hudMgr?.Get("chat");
        if (w == null || dragFrame < 70) return;
        Vector2 start = w.GlobalPosition + new Vector2(60, 10);
        void mouse(bool press, Vector2 pos) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = press, Position = pos, GlobalPosition = pos });
        if (dragStage == 0) { GD.Print($"[test] chat window at {w.Position} size {w.Size}"); Input.WarpMouse(start); mouse(true, start); dragStage = 1; dragFrame = 70; }
        else if (dragStage == 1 && dragFrame > 74)
        {
            var to = start + DragDelta;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - start });
            dragStage = 2; dragFrame = 70;
        }
        else if (dragStage == 2 && dragFrame > 74)
        {
            var to = start + DragDelta;
            mouse(false, to); dragStage = 3; dragFrame = 70;
        }
        else if (dragStage == 3 && dragFrame > 74) { GD.Print($"[test] chat window now at {w.Position} size {w.Size}"); dragStage = 4; }
    }

    /// <summary>One stat row: small icon + coloured bar with "current / max" text on top.</summary>
    Control MakeBar(string iconFile, Color fill, out ProgressBar bar, out Label text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var iconPath = ResolveFile(iconFile);
        var icon = new TextureRect { CustomMinimumSize = new Vector2(26, 26), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        var e = iconPath != null ? sprites.Get(iconPath, 1) : null;
        if (e != null) icon.Texture = e.Texture;
        row.AddChild(icon);

        bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 22), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.10f, 0.10f, 0.12f, 0.95f), BorderColor = new Color(0.35f, 0.35f, 0.45f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        text = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        text.AddThemeFontSizeOverride("font_size", 13);
        text.AddThemeColorOverride("font_outline_color", Colors.Black);
        text.AddThemeConstantOverride("outline_size", 4);
        bar.AddChild(text);
        row.AddChild(bar);
        return row;
    }

    void SetBar(ProgressBar bar, Label text, byte num)
    {
        if (bar == null) return;
        var st = client.GetNumeric(Meridian59.Common.Enums.StatGroup.Condition, num);
        if (st == null) { text.Text = "-"; bar.Value = 0; return; }
        int max = Math.Max(1, st.ValueMaximum), cur = st.ValueCurrent;
        bar.Value = Math.Clamp(cur / (double)max, 0, 1);
        text.Text = $"{cur} / {max}";
    }

    const int MinVigorToRun = 10;

    void UpdateBars()
    {
        SetBar(barHealth, txtHealth, 1);
        SetBar(barMana, txtMana, 2);
        SetBar(barVigor, txtVigor, 3);
    }

    void RebuildInventory()
    {
        if (invGrid == null) return;
        foreach (var c in invGrid.GetChildren()) { invGrid.RemoveChild(c); c.QueueFree(); }
        selectedSlot = null;   // selectedItemId is kept so the selection survives a rebuild
        foreach (var it in client.Inventory)
        {
            if (false) GD.Print($"[inv] {client.Resolve(it.NameRID)} file={client.Resolve(it.OverlayFileRID)} anim={it.Animation?.GetType().Name} group={(it.Animation as AnimationNone)?.Group} flags={it.Flags}");
            var path = ResolveFile(client.Resolve(it.OverlayFileRID));
            ushort group = (it.Animation is AnimationNone an) ? an.Group : (ushort)1;
            if (group == 0) group = 1;
            var e = path != null ? sprites.Get(path, group, 0, it.ColorTranslation) : null;
            var slot = new InvSlot
            {
                ItemId = it.ID,
                ItemName = client.Resolve(it.NameRID) ?? "item",
                Icon = e?.Texture,
                Count = (int)it.Count,
                Equipped = client.Equipped.Contains(it.ID),
                TooltipText = client.Resolve(it.NameRID) ?? "item",
            };
            slot.Clicked = s => SelectSlot(s);
            slot.Activated = s => { SelectSlot(s); ToggleUse(s.ItemId); };
            slot.RightClicked = s => { SelectSlot(s); ShowItemWindow(s); };
            invGrid.AddChild(slot);
            if (it.ID == selectedItemId) SelectSlot(slot);
        }
    }

    void SelectSlot(InvSlot s)
    {
        if (selectedSlot != null) { selectedSlot.Selected = false; selectedSlot.QueueRedraw(); }
        selectedSlot = s; selectedItemId = s.ItemId; s.Selected = true; s.QueueRedraw();
        if (invInfo != null) invInfo.Text = s.ItemName + (s.Equipped ? "  (equipped)" : "");
    }

    /// <summary>Find the nearest object sprite under a screen position (0 = none).</summary>
    uint PickObjectAt(Vector2 screen)
    {
        uint best = 0; float bestDist = float.MaxValue;
        var right = cam.GlobalTransform.Basis.X;
        foreach (var kv in objSprites)
        {
            var sp = kv.Value;
            if (!sp.Visible || sp.Texture == null) continue;
            var basePos = sp.GlobalPosition;
            if (cam.IsPositionBehind(basePos)) continue;
            float h = sp.Texture.GetHeight() * sp.PixelSize, w = sp.Texture.GetWidth() * sp.PixelSize;
            var bottom = cam.UnprojectPosition(basePos);
            var top = cam.UnprojectPosition(basePos + Vector3.Up * h);
            var side = cam.UnprojectPosition(basePos + right * (w / 2f));
            float halfW = Mathf.Abs(side.X - bottom.X);
            var rect = new Rect2(Mathf.Min(bottom.X, top.X) - halfW, Mathf.Min(top.Y, bottom.Y), halfW * 2, Mathf.Abs(bottom.Y - top.Y));
            if (!rect.HasPoint(screen)) continue;
            float d = cam.GlobalPosition.DistanceTo(basePos);
            if (d < bestDist) { bestDist = d; best = kv.Key; }
        }
        return best;
    }

    // test hook: click an object through the real input pipeline (look), then double-click it (pick up attempt)
    void DrivePickTest()
    {
        double t = Time.GetTicksMsec() / 1000.0;
        if (pickTestAt == 0) pickTestAt = t;
        if (t - pickTestAt < 1.0 || objSprites.Count == 0) return;
        // aim at the NPC with the biggest sprite (innkeeper) by turning the camera toward it
        uint id = 0; float bestH = 0;
        foreach (var kv in objSprites) { float hh = kv.Value.Texture.GetHeight(); if (hh > bestH) { bestH = hh; id = kv.Key; } }
        var sp = objSprites[id];
        if (pickTestStage == 0)
        {
            var d = sp.GlobalPosition - cam.GlobalPosition;
            yaw = Mathf.Atan2(-d.X, -d.Z); pitch = 0; ApplyLook();
            pickTestStage = 1; pickTestAt = t;
            GD.Print($"[test] turned to object {id}");
        }
        else if (pickTestStage == 1 && t - pickTestAt > 0.5)
        {
            var c = cam.UnprojectPosition(sp.GlobalPosition + Vector3.Up * (sp.Texture.GetHeight() * sp.PixelSize / 2f));
            GD.Print($"[test] clicking at {c} -> pick says {PickObjectAt(c)}");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = c, GlobalPosition = c });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = c, GlobalPosition = c });
            pickTestStage = 2; pickTestAt = t;
        }
        else if (pickTestStage == 2 && t - pickTestAt > 1.0)
        {
            var c = cam.UnprojectPosition(sp.GlobalPosition + Vector3.Up * (sp.Texture.GetHeight() * sp.PixelSize / 2f));
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = c, GlobalPosition = c });
            pickTestStage = 3;
        }
    }

    void ToggleUse(uint id)
    {
        if (client.Equipped.Contains(id)) client.UnuseItem(id); else client.UseItem(id);
    }

    void ShowItemWindow(InvSlot s)
    {
        itemPic.Texture = s.Icon;
        itemName.Text = s.ItemName;
        itemDesc.Text = "...";
        if (lookActions != null) lookActions.Visible = false;   // inventory items have no trade buttons
        itemWin.Visible = true;
        itemWin.GetParent().MoveChild(itemWin, -1);
        client.LookAt(s.ItemId);
    }

    void OnLookReceived(uint id, string name, string desc, string inscription, uint overlayRid, ushort group)
    {
        if (silentLooks.Remove(id))
        {   // hover description request: keep the text for the tooltip, do not open the Look window
            lookDesc[id] = ((desc ?? "") + (string.IsNullOrEmpty(inscription) ? "" : "\n" + inscription)).Trim();
            if (listRows.TryGetValue(id, out var lr)) lr.TooltipText = RowTooltip(lr, (string)lr.GetMeta("extra", ""));
            return;
        }
        GD.Print($"[look] #{id} name='{name}' desc='{(desc ?? "<null>").Replace("\n", " ")}' inscription='{inscription}'");
        // every look (inventory right-click or a click on something in the world) is shown in the Look window
        if (itemWin == null) { if (!string.IsNullOrEmpty(desc)) AddChatLine(desc, "System"); return; }
        var path = overlayRid != 0 ? ResolveFile(client.Resolve(overlayRid)) : null;
        var e = path != null ? sprites.Get(path, group == 0 ? 1 : group) : null;
        if (e != null) itemPic.Texture = e.Texture;
        if (!string.IsNullOrEmpty(name)) itemName.Text = name;
        string text = desc ?? "";
        if (!string.IsNullOrEmpty(inscription)) text += "\n\n" + inscription;
        itemDesc.Text = text;
        itemDesc.ScrollToLine(0);
        BuildLookActions(id, (name ?? "") + " " + text);
        itemWin.Visible = true;
        itemWin.GetParent().MoveChild(itemWin, -1);
    }
}
