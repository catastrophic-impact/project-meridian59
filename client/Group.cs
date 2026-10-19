using System.Collections.Generic;
using Godot;
using static GameClient;

/// <summary>Group window (DAoC style: a row per member with health + mana bars, click = target), companion / group management window (top bar "Group" button),
/// the invitation prompt and the health bar of the target. Needs our own server (extension messages); on other servers the windows simply stay empty.</summary>
public partial class Main
{
    VBoxContainer groupRows, companionRows; HudWindow groupWin, companionWin, inviteWin; Label inviteText, companionInfo;
    bool compCmdSent; uint inviteFrom; ProgressBar targetHpBar; double targetHpAt; uint targetHpFor;

    static readonly string[] RoleNames = { "", "Mage", "Healer", "Tank" };

    void BuildGroupUi()
    {
        // --- group window
        var gbox = new VBoxContainer(); gbox.AddThemeConstantOverride("separation", 3);
        groupRows = new VBoxContainer(); groupRows.AddThemeConstantOverride("separation", 3);
        gbox.AddChild(groupRows);
        var gscroll = new ScrollContainer { ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; gbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; gscroll.AddChild(gbox);
        groupWin = hudMgr.Add("group", "Group", gscroll, new Vector2(0.01f, 0.36f), new Vector2(250, 200), visibleByDefault: false);
        groupWin.Visible = false;

        // --- companion / group management window
        var cbox = new VBoxContainer(); cbox.AddThemeConstantOverride("separation", 6);
        cbox.AddChild(new Label { Text = "Companion bots", Modulate = new Color(1, 1, 1, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var summon = new HBoxContainer(); summon.AddThemeConstantOverride("separation", 4);
        for (int role = 1; role <= 3; role++)
        {
            int rr = role;
            var b = new Button { Text = RoleNames[role], ClipText = true, TooltipText = $"Call a {RoleNames[role].ToLower()} companion (it joins your group)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.Pressed += () => client.CompanionCall(rr);
            summon.AddChild(b);
        }
        cbox.AddChild(summon);
        var dismiss = new Button { Text = "Dismiss all", ClipText = true, TooltipText = "Send every companion of yours away" };
        dismiss.Pressed += () => { foreach (var m in new List<GroupMember>(client.Group)) if (m.Kind == 2) client.CompanionDismiss(m.Id); };
        cbox.AddChild(dismiss);
        // orders: double-click to use, drag onto the hotbar
        cbox.AddChild(new Label { Text = "Orders (double-click, or drag onto the hotbar)", Modulate = new Color(1, 1, 1, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var (kind, name) in new[] { ("comp_attack", "Bots: Attack my target"), ("comp_passive", "Bots: Passive (follow only)"), ("comp_assist", "Bots: Assist (fight)") })
        {
            var row = new ListRow { ItemName = name, Kind = kind, TooltipText = CommandTip(kind) };
            row.Activated = r => CompanionCommand(kind);
            cbox.AddChild(row);
        }
        cbox.AddChild(new HSeparator());
        cbox.AddChild(new Label { Text = "Group", Modulate = new Color(1, 1, 1, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var grow = new HBoxContainer(); grow.AddThemeConstantOverride("separation", 4);
        var inv = new Button { Text = "Invite target", ClipText = true, TooltipText = "Invite the player you have targeted (click them first)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inv.Pressed += () => { if (targetId != 0 && targetId != client.PlayerId) client.GroupInvite(targetId); else AddChatLine("Click a player first, then press Invite target.", "System"); };
        var leave = new Button { Text = "Leave group", ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        leave.Pressed += () => client.GroupLeave();
        grow.AddChild(inv); grow.AddChild(leave);
        cbox.AddChild(grow);
        companionRows = new VBoxContainer(); companionRows.AddThemeConstantOverride("separation", 2);
        cbox.AddChild(companionRows);
        companionInfo = new Label { Text = "", Modulate = new Color(1, 1, 1, 0.6f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        cbox.AddChild(companionInfo);
        var cscroll = new ScrollContainer { ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; cbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; cscroll.AddChild(cbox);
        companionWin = hudMgr.Add("companions", "Group & companions", cscroll, new Vector2(0.20f, 0.14f), new Vector2(320, 380), visibleByDefault: false);
        companionWin.Visible = false;

        // --- invitation prompt
        var ibox = new VBoxContainer(); ibox.AddThemeConstantOverride("separation", 6);
        inviteText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var irow = new HBoxContainer(); irow.AddThemeConstantOverride("separation", 6);
        var yes = new Button { Text = "Accept", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; yes.Pressed += () => { client.GroupAccept(); inviteWin.Visible = false; };
        var no = new Button { Text = "Decline", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; no.Pressed += () => { client.GroupDecline(); inviteWin.Visible = false; };
        irow.AddChild(yes); irow.AddChild(no);
        ibox.AddChild(inviteText); ibox.AddChild(irow);
        inviteWin = hudMgr.Add("invite", "Group invitation", ibox, new Vector2(0.40f, 0.30f), new Vector2(280, 100), resizable: false, visibleByDefault: false);
        inviteWin.Visible = false;

        if (System.Environment.GetEnvironmentVariable("M59_GROUPTEST") == "accept") client.GroupInviteReceived += (from, name) => { GD.Print($"[group] invite from {name}, accepting"); client.GroupAccept(); };
        client.GroupChanged += () => { if (AnimLog || System.Environment.GetEnvironmentVariable("M59_GROUPTEST") != null) GD.Print($"[group] state: leader {client.GroupLeader}, " + string.Join(", ", client.Group.ConvertAll(m => $"{m.Name}({m.Kind},{m.Role}) {m.Hp}/{m.MaxHp} mana {m.Mana}/{m.MaxMana}"))); };
        client.GroupChanged += OnGroupChanged;
        client.GroupChanged += RefreshCompanionLabels;
        client.GroupInviteReceived += (from, name) => { inviteFrom = from; inviteText.Text = $"{name} invites you to join a group."; inviteWin.Visible = true; };
        client.TargetHealthReceived += (id, pct) => { if (id == targetId && targetHpBar != null) { targetHpBar.Value = pct / 100.0; targetHpBar.Visible = true; } };
    }

    void OnGroupChanged()
    {
        if (groupRows == null) return;
        foreach (var c in groupRows.GetChildren()) { groupRows.RemoveChild(c); c.QueueFree(); }
        foreach (var c in companionRows.GetChildren()) { companionRows.RemoveChild(c); c.QueueFree(); }
        var members = client.Group;
        bool imLeader = client.GroupLeader == client.PlayerId;
        groupWin.Visible = members.Count > 1 || (members.Count == 1 && groupWin.Visible && false);
        foreach (var m in members)
        {
            var row = new Button { ClipContents = true, Flat = true, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 40), TooltipText = "Click to target. Heal or buff them with the usual spells." };
            uint id = m.Id; row.Pressed += () => TargetMember(id);
            var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 1); v.MouseFilter = Control.MouseFilterEnum.Ignore; v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            string tag = m.Kind == 2 ? $"  [{(m.Role >= 1 && m.Role <= 3 ? RoleNames[m.Role] : "bot")}]" : "";
            var name = new Label { Text = (m.Id == client.GroupLeader ? "* " : "") + m.Name + tag, MouseFilter = Control.MouseFilterEnum.Ignore };
            name.AddThemeFontSizeOverride("font_size", 14);
            v.AddChild(name);
            v.AddChild(MakeMiniBar(m.Hp, m.MaxHp, new Color(0.8f, 0.15f, 0.15f)));
            if (m.MaxMana > 0) v.AddChild(MakeMiniBar(m.Mana, m.MaxMana, new Color(0.25f, 0.45f, 0.95f)));
            row.AddChild(v);
            groupRows.AddChild(row);
            if (imLeader && m.Id != client.PlayerId)
            {
                var kick = new Button { ClipText = true, Text = m.Kind == 2 ? $"Dismiss {m.Name}" : $"Remove {m.Name}" };
                kick.Pressed += () => client.GroupKick(id);
                companionRows.AddChild(kick);
            }
        }
        companionInfo.Text = members.Count > 1 ? $"In a group of {members.Count}." : "Not in a group.";
    }

    static ProgressBar MakeMiniBar(int cur, int max, Color col)
    {
        var b = new ProgressBar { MinValue = 0, MaxValue = 1, Value = max > 0 ? System.Math.Clamp(cur / (double)max, 0, 1) : 0, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 9), MouseFilter = Control.MouseFilterEnum.Ignore };
        b.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = col });
        b.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f, 0.9f) });
        return b;
    }

    void TargetMember(uint id)
    {
        if (id == client.PlayerId || client.Objects.Exists(o => o.ID == id)) targetId = id;
        else AddChatLine("That group member is not in this room.", "System");
    }

    /// <summary>Once a second: ask the server for the health of the target (our own server only).</summary>
    double groupTestAt;
    void PollTargetHealth()
    {
        if (System.Environment.GetEnvironmentVariable("M59_GROUPTEST") == "invite" && roomShown && groupTestAt == 0) groupTestAt = Time.GetTicksMsec() + 6000;
        var gt = System.Environment.GetEnvironmentVariable("M59_GROUPTEST");
        if (gt != null && gt.StartsWith("companion") && roomShown && groupTestAt == 0) groupTestAt = Time.GetTicksMsec() + 6000;
        if (groupTestAt > 0 && Time.GetTicksMsec() > groupTestAt && gt != null && gt.StartsWith("companion"))
        {
            groupTestAt = -1;
            int role = gt.EndsWith("healer") ? 2 : gt.EndsWith("tank") ? 3 : 1;
            GD.Print($"[group] calling companion role {role}"); client.CompanionCall(role);
        }
        if (groupTestAt > 0 && Time.GetTicksMsec() > groupTestAt)
        {
            groupTestAt = -1;
            foreach (var o in client.Objects) if (o.ID != client.PlayerId && o.Flags.IsPlayer) { GD.Print($"[group] inviting {client.Resolve(o.NameRID)} #{o.ID}"); client.GroupInvite(o.ID); break; }
        }
        var cc = System.Environment.GetEnvironmentVariable("M59_COMPCMD");
        if (cc != null && roomShown && Time.GetTicksMsec() > 16000 && !compCmdSent) { compCmdSent = true; GD.Print("[group] test command " + cc); CompanionCommand(cc); }
        if (!client.ServerHasExtensions && Time.GetTicksMsec() < 8000) { /* first tries are allowed to detect the extension */ }
        if (targetHpBar == null) return;
        if (targetId == 0 || targetId == client.PlayerId) { targetHpBar.Visible = false; return; }
        if (targetHpFor != targetId) { targetHpFor = targetId; targetHpBar.Visible = false; targetHpAt = 0; }
        double now = Time.GetTicksMsec();
        if (now < targetHpAt) return;
        targetHpAt = now + 1000;
        if (client.ServerHasExtensions || now < 20000) client.RequestTargetHealth(targetId);
    }
}
