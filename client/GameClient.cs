using System;
using System.Collections.Generic;
using System.IO;
using Meridian59.Common;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Files.RSB;
using Meridian59.Protocol;
using Meridian59.Protocol.GameMessages;

/// <summary>
/// Headless game session: logs in, picks the first character and tracks the current room.
/// Call Update() every frame; it drains the network queue and raises events on the caller's thread.
/// </summary>
public class GameClient
{
    public enum Phase { Offline, Connecting, LoggingIn, EnteringGame, Playing, Failed }

    public Phase State { get; private set; } = Phase.Offline;
    public string Status { get; private set; } = "Not connected";
    public StringDictionary Strings { get; } = new StringDictionary();
    public RoomInfo Room { get; private set; }
    public List<RoomObject> Objects { get; private set; } = new List<RoomObject>();
    public uint PlayerId { get; private set; }
    public string CharacterName { get; private set; } = "";
    public RoomObject Player => Objects.Find(o => o.ID == PlayerId);

    /// <summary>Raised once the player's room info AND its contents have arrived (and again when the room changes).</summary>
    public event Action RoomReady;
    public event Action<string> Log;
    /// <summary>World coordinates are client fineness (1024 per grid square): (kodWord - 64) * 16.</summary>
    public event Action<RoomObject> ObjectAdded;
    public event Action<uint> ObjectRemoved;
    /// <summary>The server changed an existing object's appearance (BP_CHANGE); the RoomObject in Objects is already updated.</summary>
    public event Action<uint> ObjectChanged;
    public event Action<uint, float, float, byte> ObjectMoved;   // id, worldX, worldY, speed
    public event Action<uint, ushort> ObjectTurned;              // id, angle units (0..4095)
    /// <summary>Chat/system text from the server: (text, kind) where kind is the transmission type name, "System" or "Server".</summary>
    public event Action<string, string> ChatReceived;

    /// <summary>Latest stats by group then stat number (Condition = health/mana/vigor, Attributes = might/intellect/..., etc.).</summary>
    public Dictionary<StatGroup, Dictionary<byte, Stat>> Stats { get; } = new Dictionary<StatGroup, Dictionary<byte, Stat>>();
    public event Action StatsChanged;

    // inventory: items in the order the server sent them; Equipped = ids currently in use ('use list')
    public List<InventoryObject> Inventory { get; private set; } = new List<InventoryObject>();
    public HashSet<uint> Equipped { get; } = new HashSet<uint>();
    public event Action InventoryChanged;
    /// <summary>Server's answer to a look request: (object id, name, description, inscription).</summary>
    public event Action<uint, string, string, string, uint, ushort> LookReceived;   // + picture resource id and animation group

    // spells (with school and target count) and skills, as the server sends them
    public List<SpellObject> Spells { get; private set; } = new List<SpellObject>();
    public List<ObjectBase> Skills { get; private set; } = new List<ObjectBase>();
    public event Action SpellsChanged, SkillsChanged;
    /// <summary>First-person hand / weapon overlay (BP_PLAYER_OVERLAY): slot = Id (1 left hand, 2 right hand), hotspot, file, animation.</summary>
    public event Action<PlayerOverlay> PlayerOverlayReceived;
    /// <summary>Account type from the login result (server: 0 user, 1 admin, 2 DM, 3 guest). Admin tools are offered for 1 and 2.</summary>
    public byte AccountTypeByte { get; private set; }
    public bool IsAdmin => AccountTypeByte == 1 || AccountTypeByte == 2;
    /// <summary>Run a server admin console command through the game connection (needs an admin account; same syntax as the admin port).</summary>
    /// <summary>Raised when an admin "create object" command is answered with the new object's id.</summary>
    public event Action<uint> AdminObjectCreated;
    public void SendAdmin(string command) { if (conn != null && IsAdmin) { Log?.Invoke("admin > " + command); Send(new ReqAdminMessage(command)); } }
    /// <summary>Admin: teleport ourselves to a room number (RID).</summary>
    public void TeleportTo(int rid) => SendAdmin($"send object {PlayerId} TeleportTo RID int {rid}");
    public void RequestSpells() { if (conn != null) Send(new SendSpellsMessage()); }
    public void RequestSkills() { if (conn != null) Send(new SendSkillsMessage()); }
    public void Cast(uint spellId, params uint[] targets)
    {
        if (conn == null) return;
        var ids = new ObjectID[targets.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = new ObjectID(targets[i]);
        Log?.Invoke($"cast {spellId} -> [{string.Join(",", targets)}]");
        Send(new ReqCastMessage(spellId, ids));
    }

    public void RequestInventory() { if (conn != null) Send(new ReqInventoryMessage()); }
    public void UseItem(uint id) { if (conn != null) Send(new ReqUseMessage(id)); }
    public void UnuseItem(uint id) { if (conn != null) Send(new ReqUnuseMessage(id)); }
    public void DropItem(uint id, uint count = 0) { if (conn != null) Send(new ReqDropMessage(new ObjectID(id, count))); }
    public void PickUp(uint id, uint count = 0) { if (conn != null) Send(new ReqGetMessage(new ObjectID(id, count))); }
    public void Attack(uint id) { if (conn != null) { Log?.Invoke("attack -> " + id); Send(new ReqAttackMessage(ReqAttackMessage.ATTACK_NORMAL, id)); } }
    /// <summary>Space at a door/portal: tell the server where we stand, then ask it to take us through.</summary>
    public void GoThrough(float worldX, float worldY) { if (conn == null) return; SendMove(worldX, worldY, false); Send(new ReqGoMessage()); }
    /// <summary>Sit down and rest (faster regeneration; the server stops it when you move or fight). Official UC_REST / UC_STAND commands.</summary>
    public void Rest() { if (conn != null) Send(new UserCommandMessage(new Meridian59.Data.Models.UserCommandRest(), Strings)); }
    public void Stand() { if (conn != null) Send(new UserCommandMessage(new Meridian59.Data.Models.UserCommandStand(), Strings)); }
    public void LookAt(uint id) { if (conn != null) Send(new ReqLookMessage(new ObjectID(id))); }

    public StatNumeric GetNumeric(StatGroup group, byte num) =>
        Stats.TryGetValue(group, out var g) && g.TryGetValue(num, out var s) ? s as StatNumeric : null;

    public void RequestStats(StatGroup group)
    {
        if (conn != null) Send(new SendStatsMessage(group));
    }

    public void SendSay(string text)
    {
        if (conn == null || string.IsNullOrWhiteSpace(text)) return;
        var type = ChatTransmissionType.Normal;
        if (text == "/rest") { Rest(); return; }
        if (text == "/stand") { Stand(); return; }
        // simple prefixes: /y yell, /e emote, /g group... (everything else is plain speech)
        if (text.StartsWith("/y ")) { type = ChatTransmissionType.Yell; text = text.Substring(3); }
        else if (text.StartsWith("/e ")) { type = ChatTransmissionType.Emote; text = text.Substring(3); }
        else if (text.StartsWith("/g ")) { type = ChatTransmissionType.Group; text = text.Substring(3); }
        Send(new SayToMessage(type, text));
    }

    public static float KodToWorld(ushort k) => (k - 64) * 16f;
    public static ushort WorldToKod(float w) => (ushort)((int)Math.Round(w / 16f) + 64);

    public void SendMove(float worldX, float worldY, bool run)
    {
        if (conn == null || Room == null) return;
        Send(new ReqMoveMessage(WorldToKod(worldX), WorldToKod(worldY), (MovementSpeed)(run ? 36 : 18), Room.RoomID));
    }

    public void SendTurn(ushort angleUnits)
    {
        if (conn == null) return;
        Send(new ReqTurnMessage(angleUnits, PlayerId));
    }

    // identify as the Ogre client does (version 90.6, resources version 10016); official servers check these
    public byte VersionMajor = 90, VersionMinor = 6;
    public uint ResourcesVersion = 10016;

    /// <summary>Optional: when set and the first character slot is brand new, create it with this name (used by headless bots).
    /// Build: "mage", "warrior" or "hybrid". The Godot client leaves this null.</summary>
    public string NewCharacterName, NewCharacterBuild = "hybrid";
    public bool NewCharacterFemale;
    ObjectID newSlot;

    /// <summary>PlayWave: sound file name (kod name, usually .wav), source object id (0 = none), flags (1 loop), row, col (grid squares), radius, max volume (0..50).</summary>
    public event Action<string, uint, byte, int, int, int, int> SoundRequested;
    /// <summary>PlayMusic/PlayMidi: music file name (kod name, usually .mid) or null to stop the music.</summary>
    public event Action<string> MusicRequested;
    /// <summary>BP_ADD_BG_OVERLAY / BP_CHANGE_BG_OVERLAY: sun, moon ... placed by angle and height around the viewer.</summary>
    public event Action<BackgroundOverlay> BackgroundOverlayChanged;
    /// <summary>BP_BACKGROUND: name of the sky picture for outdoor rooms (e.g. "1skya.bgf"), null = none.</summary>
    public event Action<string> BackgroundChanged;
    /// <summary>Ambient room light, the viewer's own light (both 0-255).</summary>
    public event Action<byte, byte> LightingChanged;
    // ---- custom server extension (groups, companions, target health): BP_EXT_GAME (250) from the server, UC_EXT_GAME (100) inside BP_USERCOMMAND to it
    public class GroupMember { public uint Id; public byte Kind, Role; public string Name = ""; public int Hp, MaxHp, Mana, MaxMana; }
    public uint GroupLeader; public List<GroupMember> Group = new List<GroupMember>();
    public event Action GroupChanged;
    public event Action<uint, string> GroupInviteReceived;

    // enchantments (buffs / debuffs): the lists come from BP_ADD/REMOVE_ENCHANTMENT, the time left from our extension message
    public List<ObjectBase> AvatarBuffs { get; private set; } = new List<ObjectBase>();
    public List<ObjectBase> RoomBuffs { get; private set; } = new List<ObjectBase>();
    public class BuffTime { public long ExpiresAt; public bool Timed, Harmful; }
    public Dictionary<uint, BuffTime> BuffTimes { get; } = new Dictionary<uint, BuffTime>();
    public event Action BuffsChanged;
    /// <summary>Our server extension: damage (kind 0) or healing (kind 1) of an object: victim, attacker (= victim for heals), amount, kind.</summary>
    public event Action<uint, uint, int, byte> CombatNumberReceived;
    public event Action<uint, int> TargetHealthReceived;
    public bool ServerHasExtensions;       // set once any extension message arrived (this is our own server)

    void SendExt(byte sub, uint obj, int arg)
    {
        if (conn == null) return;
        var data = new byte[10];
        data[0] = 100; data[1] = sub;
        BitConverter.GetBytes(obj).CopyTo(data, 2); BitConverter.GetBytes(arg).CopyTo(data, 6);
        Send(new GenericGameMessage(155, data));       // BP_USERCOMMAND / UC_EXT_GAME
    }
    uint SelfOrZero(uint id) => id != 0 ? id : PlayerId;     // the server rejects unknown object ids, so send our own id when there is none
    public void GroupInvite(uint target) => SendExt(1, SelfOrZero(target), 0);
    public void GroupAccept() => SendExt(2, PlayerId, 0);
    public void GroupDecline() => SendExt(3, PlayerId, 0);
    public void GroupLeave() => SendExt(4, PlayerId, 0);
    public void GroupKick(uint member) => SendExt(5, SelfOrZero(member), 0);
    public void RequestGroup() => SendExt(6, PlayerId, 0);
    public void CompanionCall(int role) => SendExt(10, PlayerId, role);
    public void CompanionAttack(uint target) => SendExt(12, SelfOrZero(target), 0);
    public void CompanionPassive() => SendExt(13, PlayerId, 0);
    public void CompanionAssist() => SendExt(14, PlayerId, 0);
    public void CompanionDismiss(uint id) => SendExt(11, SelfOrZero(id), 0);
    public void RequestTargetHealth(uint id) => SendExt(20, SelfOrZero(id), 0);

    void HandleExt(byte[] d)
    {
        ServerHasExtensions = true;
        using var r = new System.IO.BinaryReader(new System.IO.MemoryStream(d));
        string Str() { int n = r.ReadUInt16(); return System.Text.Encoding.Latin1.GetString(r.ReadBytes(n)); }
        switch (r.ReadByte())
        {
            case 1:
                GroupLeader = r.ReadUInt32(); int count = r.ReadByte();
                var list = new List<GroupMember>();
                for (int i = 0; i < count; i++)
                    list.Add(new GroupMember { Id = r.ReadUInt32(), Kind = r.ReadByte(), Role = r.ReadByte(), Name = Str(), Hp = r.ReadInt16(), MaxHp = r.ReadInt16(), Mana = r.ReadInt16(), MaxMana = r.ReadInt16() });
                Group = list; GroupChanged?.Invoke();
                break;
            case 2: { uint from = r.ReadUInt32(); GroupInviteReceived?.Invoke(from, Str()); break; }
            case 3: { uint id = r.ReadUInt32(); TargetHealthReceived?.Invoke(id, r.ReadByte()); break; }
            case 5: { uint v = r.ReadUInt32(), a = r.ReadUInt32(); int amt = r.ReadUInt16(); byte kind = r.ReadByte(); CombatNumberReceived?.Invoke(v, a, amt, kind); break; }
            case 4:
                {
                    int n = r.ReadByte(); long now = Environment.TickCount64;
                    for (int i = 0; i < n; i++)
                    {
                        uint id = r.ReadUInt32(); int ms = r.ReadInt32(); bool bad = r.ReadByte() != 0;
                        BuffTimes[id] = new BuffTime { Timed = ms > 0, ExpiresAt = now + Math.Max(0, ms), Harmful = bad };
                    }
                    BuffsChanged?.Invoke();
                    break;
                }
        }
    }

    // trading (official offer.c / buy.c): buy lists, bank withdrawal lists and the offer / counteroffer dialogs
    public event Action<ObjectBase, TradeOfferObject[], bool> BuyListReceived;      // seller, items with prices, true = bank withdrawal list
    public event Action<ObjectBase, ObjectBase[]> OfferReceived;                    // someone offers us items (we are the receiver)
    public event Action<ObjectBase[]> OfferedReceived;                              // our own offer was accepted by the server (items we offer)
    public event Action<ObjectBase[]> CounterOfferReceived;                         // the other side answered with these items
    public event Action<ObjectBase[]> CounterOfferedReceived;                       // the server confirms the items we gave in return
    public event Action OfferCanceledReceived;
    public void TradeRequestBuy(uint seller) { if (conn != null) Send(new ReqBuyMessage(seller)); }
    public void TradeBuyItems(uint seller, ObjectID[] items) { if (conn != null) Send(new ReqBuyItemsMessage(seller, items)); }
    public void TradeRequestWithdraw(uint banker) { if (conn != null) Send(new ReqWithdrawAlMessage(banker)); }
    public void TradeWithdrawItems(uint banker, ObjectID[] items) { if (conn != null) Send(new ReqWithDrawAlItemsMessage(banker, items)); }
    public void TradeOffer(uint to, ObjectID[] items) { if (conn != null) Send(new ReqOfferMessage(new ObjectID(to), items)); }
    public void TradeDeposit(uint to, ObjectID[] items) { if (conn != null) Send(new ReqDepositMessage(new ObjectID(to), items)); }
    public void TradeAccept() { if (conn != null) Send(new AcceptOfferMessage()); }
    public void TradeCancel() { if (conn != null) Send(new CancelOfferMessage()); }
    public void TradeCounter(ObjectID[] items) { if (conn != null) Send(new ReqCounterOfferMessage(items)); }
    public event Action<ushort, Animation, byte> WallAnimated;   // sidedef server id, animation, end action (0 none, 1 passable, 2 impassable, 3 invisible + passable)
    public event Action<byte, ushort, ushort, byte> SectorMoved;   // type (4 floor lift, 5 ceiling lift), server sector id, destination height (kod units), speed (kod units per second; 0 = at once)
    public event Action<int, int> SunChanged;       // intensity 0-64, angle 0-4095
    public byte AmbientLight, ViewerLight;
    void SetLight(byte ambient, byte viewer) { AmbientLight = ambient; ViewerLight = viewer; LightingChanged?.Invoke(ambient, viewer); }
    /// <summary>BP_SHOOT: a bitmap flies from one object to another (spells, arrows).</summary>
    public event Action<Projectile> ProjectileFired;
    /// <summary>BP_EFFECT: screen effect (pain, whiteout, blind, shake, ...).</summary>
    public event Action<Effect> EffectReceived;
    /// <summary>A new room is being entered (looping sounds of the old room must stop).</summary>
    public event Action RoomChanging;

    /// <summary>The server sent the character creation options (faces, colours, spells, skills); the UI should now show its creation screen.</summary>
    public event Action<CharCreationInfo> CharCreationRequested;
    /// <summary>The server refused the name/gender (taken, forbidden word, ...).</summary>
    public event Action CharCreationRejected;

    /// <summary>Send the chosen character to the server; on success the game is entered automatically.</summary>
    public void CreateCharacter(string name, bool female, uint[] faceIds, byte hairColor, byte skinColor, uint[] attrs, uint[] spells, uint[] skills)
    {
        CharacterName = name;
        Send(new SystemMessage(new Meridian59.Protocol.SubMessage.SubMessageNewCharInfo(
            newSlot, name, "", (byte)(female ? 2 : 1), faceIds, hairColor, skinColor, attrs, spells, skills)));
    }

    ServerConnection conn;
    string user, password;
    bool sentLogin, sentGame, sentChars, haveInfo, haveContents, requestedStats;

    public bool LoadStrings(string rsbPath)
    {
        try
        {
            Strings.Clear();
            var rsb = new RsbFile();
            rsb.Load(rsbPath);
            foreach (var e in rsb.StringResources) Strings.TryAdd(e.ID, e.Text, LanguageCode.English);
            Log?.Invoke($"loaded {Strings.Count} string resources from {Path.GetFileName(rsbPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Fail("Cannot load string resources (" + rsbPath + "): " + ex.Message);
            return false;
        }
    }

    public string Resolve(uint rid) => Strings.TryGetValue(rid, out var s) ? s : null;

    public void Connect(string host, ushort port, string username, string pw)
    {
        user = username; password = pw;
        sentLogin = sentGame = sentChars = haveInfo = haveContents = requestedStats = false;
        Room = null; Objects = new List<RoomObject>(); AvatarBuffs.Clear(); RoomBuffs.Clear(); BuffTimes.Clear();
        GroupLeader = 0; Group = new List<GroupMember>(); GroupChanged?.Invoke();
        conn?.Disconnect();
        conn = new ServerConnection(Strings);
        State = Phase.Connecting;
        Status = $"Connecting to {host}:{port} ...";
        conn.Connect(host, port);
    }

    public void Disconnect()
    {
        conn?.Disconnect();
        State = Phase.Offline;
        Status = "Disconnected";
    }

    void Fail(string msg)
    {
        State = Phase.Failed;
        Status = msg;
        Log?.Invoke(msg);
    }

    public void Update()
    {
        if (conn == null) return;

        while (conn.ExceptionQueue.TryDequeue(out var ex))
            Fail("Network error: " + ex.Message);

        while (conn.ReceiveQueue.TryDequeue(out var m))
            Handle(m);
    }

    void Send(GameMessage m) => conn.SendQueue.Enqueue(m);

    static readonly bool Trace = System.Environment.GetEnvironmentVariable("M59_TRACE") == "1";
    void Handle(GameMessage m)
    {
        if (Trace) Log?.Invoke("rx " + m.GetType().Name);
        switch (m)
        {
            case GetLoginMessage when !sentLogin:
                sentLogin = true;
                State = Phase.LoggingIn; Status = "Logging in ...";
                Send(new LoginMessage(user, password, VersionMajor, VersionMinor,
                    LoginMessage.WINTYPE_NT, 6, 1, 512000000, LoginMessage.CPUTYPE_PENTIUM, 0xFFFF, 1024, 768, 0, 0, 32, 30, 0));
                break;

            case LoginFailedMessage:
                Fail("Login failed: wrong account name or password");
                break;

            case LoginModeMessageMessage lm:
                Fail("Server: " + lm.Message);
                break;

            case GetChoiceMessage when !sentGame:
                sentGame = true;
                State = Phase.EnteringGame; Status = "Entering game ...";
                Send(new ReqGameStateMessage(ResourcesVersion, VersionMajor, VersionMinor, string.Empty));
                break;

            // the server ignores game messages until it has sent its first one (epoch handshake)
            case LoadModuleMessage when !sentChars:
                sentChars = true;
                Send(new SendCharactersMessage());
                break;

            case CharactersMessage cm:
                if (cm.WelcomeInfo.Characters.Count == 0) { Fail("This account has no characters (create one on the server)"); break; }
                var first = cm.WelcomeInfo.Characters[0];
                if (first.Flags == CharSelectItem.FLAG_NEWCHAR && NewCharacterName != null)
                {
                    newSlot = new ObjectID(first.ID);
                    Send(new SystemMessage(new Meridian59.Protocol.SubMessage.SubMessageSendCharInfo()));
                    break;
                }
                if (first.Flags == CharSelectItem.FLAG_NEWCHAR)
                {   // interactive creation: ask the server for the options, the UI answers with CreateCharacter()
                    newSlot = new ObjectID(first.ID);
                    Status = "Create your character";
                    Send(new SystemMessage(new Meridian59.Protocol.SubMessage.SubMessageSendCharInfo()));
                    break;
                }
                Log?.Invoke("character: " + first.Name);
                CharacterName = first.Name;
                PlayerId = first.ID;
                Send(new UseCharacterMessage(first));
                break;

            case CharInfoMessage cimUi when NewCharacterName == null:
                CharCreationRequested?.Invoke(cimUi.CharCreationInfo);
                break;

            case CharInfoNotOkMessage:
                CharCreationRejected?.Invoke();
                break;

            case CharInfoMessage cim:
                {
                    var ci = cim.CharCreationInfo;
                    if (NewCharacterBuild == "mage") ci.SetAttributesToMage();
                    else if (NewCharacterBuild == "warrior") ci.SetAttributesToWarrior();
                    else ci.SetAttributesToHybrid();
                    var ids = NewCharacterFemale
                        ? new uint[] { ci.FemaleSkullID.Value, ci.FemaleHairIDs[0].Value, ci.FemaleEyeIDs[0].Value, ci.FemaleNoseIDs[0].Value, ci.FemaleMouthIDs[0].Value }
                        : new uint[] { ci.MaleSkullID.Value, ci.MaleHairIDs[0].Value, ci.MaleEyeIDs[0].Value, ci.MaleNoseIDs[0].Value, ci.MaleMouthIDs[0].Value };
                    Send(new SystemMessage(new Meridian59.Protocol.SubMessage.SubMessageNewCharInfo(
                        newSlot, NewCharacterName, "", (byte)(NewCharacterFemale ? 2 : 1), ids, 0, 0, ci.GetAttributesArray(), new uint[0], new uint[0])));
                }
                break;

            case CharInfoOkMessage:
                Send(new UseCharacterMessage(newSlot));
                break;

            case AddBgOverlayMessage abo:
                BackgroundOverlayChanged?.Invoke(abo.BackgroundOverlay);
                break;

            case ChangeBgOverlayMessage cbo:
                BackgroundOverlayChanged?.Invoke(cbo.BackgroundOverlay);
                break;

            case BackgroundMessage bgm:
                BackgroundChanged?.Invoke(bgm.ResourceID.Value == 0 ? null : Resolve(bgm.ResourceID.Value));
                break;

            case ShootMessage shm:
                ProjectileFired?.Invoke(shm.Projectile);
                break;

            case EffectMessage efm:
                EffectReceived?.Invoke(efm.Effect);
                break;

            case PlayWaveMessage pw:
                {
                    var pi = pw.PlayInfo;
                    SoundRequested?.Invoke(Resolve(pi.ResourceID), pi.ID, pi.PlayFlags.PlayFlags, pi.Row, pi.Column, pi.Radius, pi.MaxVolume);
                }
                break;

            case PlayMusicMessage pmu:
                MusicRequested?.Invoke(pmu.PlayInfo.ResourceID == 0 ? null : Resolve(pmu.PlayInfo.ResourceID));
                break;

            case PlayMidiMessage pmi:
                MusicRequested?.Invoke(pmi.PlayInfo.ResourceID == 0 ? null : Resolve(pmi.PlayInfo.ResourceID));
                break;

            case GenericGameMessage gx when gx.PI == 250:
                try { HandleExt(gx.Data); } catch (Exception ex) { Log?.Invoke("ext message error: " + ex.Message); }
                break;
            case BuyListMessage bl: BuyListReceived?.Invoke(bl.TradePartner, bl.OfferedItems, false); break;
            case WithdrawAlListMessage wl: BuyListReceived?.Invoke(wl.NPC, wl.Items, true); break;
            case OfferMessage om: OfferReceived?.Invoke(om.TradePartner, om.OfferedItems); break;
            case OfferedMessage od: OfferedReceived?.Invoke(od.OfferedItems); break;
            case CounterOfferMessage co: CounterOfferReceived?.Invoke(co.OfferedItems); break;
            case CounterOfferedMessage cd: CounterOfferedReceived?.Invoke(cd.OfferedItems); break;
            case OfferCanceledMessage: OfferCanceledReceived?.Invoke(); break;
            case WallAnimateMessage wam:
                WallAnimated?.Invoke(wam.SideDefServerID, wam.Animation, (byte)wam.Action);
                break;
            case SectorMoveMessage smv:
                SectorMoved?.Invoke((byte)smv.SectorMove.Type, smv.SectorMove.SectorNr, smv.SectorMove.Height, smv.SectorMove.Speed);
                break;
            case LightAmbientMessage lam:
                SetLight(lam.RoomBrightness, ViewerLight);
                break;
            case LightPlayerMessage lpm:
                SetLight(AmbientLight, lpm.CharacterBrightness);
                break;
            case LightShadingMessage lsm:
                SunChanged?.Invoke(lsm.LightShading.LightIntensity, lsm.LightShading.SpherePosition.Angle);
                break;

            case PlayerMessage pm:
                // new room (first entry or room change): wait for fresh contents
                RoomChanging?.Invoke();
                Room = pm.RoomInfo;
                if (RoomBuffs.Count > 0) { RoomBuffs.Clear(); BuffsChanged?.Invoke(); }
                SetLight(Room.AmbientLight, Room.AvatarLight);
                PlayerId = Room.AvatarID;
                haveInfo = true; haveContents = false;
                State = Phase.Playing;
                Status = "In game";
                break;

            case CreateMessage cr:
                Objects.Add(cr.NewRoomObject);
                ObjectAdded?.Invoke(cr.NewRoomObject);
                break;

            case RemoveMessage rm:
                Objects.RemoveAll(o => o.ID == rm.ObjectID);
                ObjectRemoved?.Invoke(rm.ObjectID);
                break;

            case MoveMessage mv:
                ObjectMoved?.Invoke(mv.ObjectID, KodToWorld(mv.NewCoordinateX), KodToWorld(mv.NewCoordinateY), (byte)mv.MovementSpeed);
                break;

            case TurnMessage tm:
                ObjectTurned?.Invoke(tm.ObjectID, tm.Angle);
                break;

            case SaidMessage sm:
                ChatReceived?.Invoke(sm.Message.FullString, sm.Message.TransmissionType.ToString());
                break;

            case SysMessageMessage sys:
                ChatReceived?.Invoke(sys.Message.FullString, "System");
                break;

            case StatMessage st:
                if (!Stats.TryGetValue(st.Group, out var grp)) Stats[st.Group] = grp = new Dictionary<byte, Stat>();
                grp[st.Stat.Num] = st.Stat;
                Log?.Invoke($"STAT update {st.Group} #{st.Stat.Num}" + (st.Stat is StatNumeric q ? $" = {q.ValueCurrent}/{q.ValueMaximum}" : ""));
                StatsChanged?.Invoke();
                break;

            case StatGroupMessage sg:
                {
                    var grp2 = new Dictionary<byte, Stat>();
                    foreach (var stat in sg.Stats) grp2[stat.Num] = stat;
                    Stats[sg.Group] = grp2;
                    foreach (var stat in sg.Stats)
                    {
                        var sn = stat as StatNumeric;
                        Log?.Invoke($"STATGROUP {sg.Group} num={stat.Num} {stat.Type} res={Resolve(stat.ResourceID)}" + (sn != null ? $" cur={sn.ValueCurrent} max={sn.ValueMaximum}" : ""));
                    }
                    StatsChanged?.Invoke();
                }
                break;

            case InventoryMessage inv:
                Inventory = new List<InventoryObject>(inv.InventoryObjects);
                InventoryChanged?.Invoke();
                break;

            case InventoryAddMessage ia:
                Inventory.RemoveAll(o => o.ID == ia.NewInventoryObject.ID);
                Inventory.Add(ia.NewInventoryObject);
                InventoryChanged?.Invoke();
                break;

            case InventoryRemoveMessage ir:
                Inventory.RemoveAll(o => o.ID == ir.ID.ID);
                Equipped.Remove(ir.ID.ID);
                InventoryChanged?.Invoke();
                break;

            case UseListMessage ul:
                Equipped.Clear();
                foreach (var e in ul.EquippedObjects) Equipped.Add(e.ID);
                InventoryChanged?.Invoke();
                break;

            case UseMessage um:
                Equipped.Add(um.NewEquippedItem.ID);
                InventoryChanged?.Invoke();
                break;

            case UnuseMessage un:
                Equipped.Remove(un.ID.ID);
                InventoryChanged?.Invoke();
                break;

            case LookMessage lk:
                {
                    var oi = lk.ObjectInfo;
                    var ob = oi?.ObjectBase;
                    LookReceived?.Invoke(ob?.ID ?? 0, ob != null ? Resolve(ob.NameRID) : null, oi?.Message?.FullString, oi?.Inscription?.FullString, ob?.OverlayFileRID ?? 0, (ob?.Animation as AnimationNone)?.Group ?? (ushort)1);
                }
                break;

            case RoomContentsMessage rc:
                Objects = new List<RoomObject>(rc.RoomObjects);
                haveContents = true;
                break;

            case ChangeMessage chg:
                {
                    // an object's appearance changed (equipped / removed armor, new overlays, name ...)
                    var obj = Objects.Find(o => o.ID == chg.UpdatedObject.ID);
                    if (obj != null) { obj.UpdateFromModel(chg.UpdatedObject, true); ObjectChanged?.Invoke(obj.ID); }
                    // an item in the pack changed (its count after eating one, a new name, ...)
                    var item = Inventory.Find(o => o.ID == chg.UpdatedObject.ID);
                    if (item != null) { item.UpdateFromModel(chg.UpdatedObject, true); InventoryChanged?.Invoke(); }
                }
                break;

            case AddEnchantmentMessage ae:
                {
                    var list = ae.BuffType == BuffType.RoomBuff ? RoomBuffs : AvatarBuffs;
                    list.RemoveAll(b => b.ID == ae.NewBuffObject.ID); list.Add(ae.NewBuffObject);
                    BuffsChanged?.Invoke();
                    break;
                }
            case RemoveEnchantmentMessage re:
                {
                    (re.BuffType == BuffType.RoomBuff ? RoomBuffs : AvatarBuffs).RemoveAll(b => b.ID == re.ID);
                    if (re.BuffType != BuffType.RoomBuff) BuffTimes.Remove(re.ID);
                    BuffsChanged?.Invoke();
                    break;
                }

            case PlayerOverlayMessage po:
                PlayerOverlayReceived?.Invoke(po.HandItemObject);
                break;

            case SpellsMessage sp:
                Spells = new List<SpellObject>(sp.SpellObjects);
                SpellsChanged?.Invoke();
                break;
            case SpellAddMessage sa:
                Spells.RemoveAll(o => o.ID == sa.NewSpellObject.ID); Spells.Add(sa.NewSpellObject);
                SpellsChanged?.Invoke();
                break;
            case SpellRemoveMessage sr:
                Spells.RemoveAll(o => o.ID == sr.ID.ID);
                SpellsChanged?.Invoke();
                break;
            case SkillsMessage sk:
                Skills = new List<ObjectBase>(sk.Skills);
                SkillsChanged?.Invoke();
                break;
            case SkillAddMessage ska:
                Skills.RemoveAll(o => o.ID == ska.Skill.ID); Skills.Add(ska.Skill);
                SkillsChanged?.Invoke();
                break;
            case SkillRemoveMessage skr:
                Skills.RemoveAll(o => o.ID == skr.ID.ID);
                SkillsChanged?.Invoke();
                break;

            case MessageMessage mm:
                // BP_MESSAGE: gameplay feedback such as "You hit the ant" / "You are too far away"
                ChatReceived?.Invoke(mm.Message.FullString, "Message");
                break;

            case LoginOKMessage lok:
                AccountTypeByte = (byte)lok.AccountType;
                break;

            case AdminMessage adm:
                {
                    var cm0 = System.Text.RegularExpressions.Regex.Match(adm.Message ?? "", @"Created object (\d+)");
                    if (cm0.Success) AdminObjectCreated?.Invoke(uint.Parse(cm0.Groups[1].Value));
                }
                foreach (var l in (adm.Message ?? "").Split('\n'))
                {
                    var t = l.TrimEnd('\r');
                    // hide the console plumbing (command echo, "return from OBJECT ..." blocks); keep results and errors
                    if (t.Trim() == "" || t.StartsWith("> ") || t.StartsWith(":<") || t == ":>" || t.StartsWith(": ")) continue;
                    ChatReceived?.Invoke(t, "Admin");
                }
                break;

            case LoadModuleMessage: case EchoPingMessage: case GameStateMessage: case CreditsMessage:
                break;

            default:
                Log?.Invoke("unhandled message " + m.GetType().Name);
                break;
        }

        if (haveInfo && haveContents)
        {
            haveInfo = haveContents = false;
            RoomReady?.Invoke();
            if (!requestedStats) { requestedStats = true; RequestStats(StatGroup.Condition); RequestStats(StatGroup.Attributes); RequestInventory(); RequestSpells(); RequestSkills(); }
        }
    }
}
