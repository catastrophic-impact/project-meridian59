using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Meridian59.Data.Models;
using static GameClient;

/// <summary>Top bar windows for the official "who" list, mail and guild (original: the mailnews module, WhoDialogProc in msgfiltr.c, merintr guild dialogs).
/// Mail works like the original client: the server hands mail over when asked and the client keeps it (userdata/mail_&lt;character&gt;.json).</summary>
public partial class Main
{
    // ---------------------------------------------------------------- who
    ItemList whoList; Label whoCount; HudWindow whoWin;

    void BuildWhoUi()
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4);
        whoCount = new Label { Text = "Players online: 0", Modulate = new Color(1, 1, 1, 0.7f) };
        whoList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 120), SelectMode = ItemList.SelectModeEnum.Single };
        whoList.ItemActivated += i => ComposeMail(whoList.GetItemText((int)i), "");
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
        var refresh = new Button { Text = "Refresh", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; refresh.Pressed += () => client.RequestPlayers();
        var mail = new Button { Text = "Send mail", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Write a letter to the selected player (or double-click a name)" };
        mail.Pressed += () => { var s = whoList.GetSelectedItems(); if (s.Length > 0) ComposeMail(whoList.GetItemText(s[0]), ""); };
        row.AddChild(refresh); row.AddChild(mail);
        box.AddChild(whoCount); box.AddChild(whoList); box.AddChild(row);
        whoWin = hudMgr.Add("who", "Who is online", box, new Vector2(0.01f, 0.20f), new Vector2(240, 320), visibleByDefault: false);
        whoWin.Visible = false;
        client.OnlineChanged += FillWho;
    }

    void ToggleWho()
    {
        hudMgr.Toggle("who");
        if (whoWin.Visible) client.RequestPlayers();
    }

    void FillWho()
    {
        if (whoList == null) return;
        whoList.Clear();
        foreach (var p in client.Online.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)) whoList.AddItem(p.name);
        whoCount.Text = "Players online: " + client.Online.Count;
    }

    // ---------------------------------------------------------------- mail
    class MailEntry : MailItem { public bool Read; }
    readonly List<MailEntry> mails = new List<MailEntry>();
    ItemList mailList; RichTextLabel mailText; Label mailHead; HudWindow mailWin, mailNewWin; TextureButton mailBtn;
    LineEdit mailTo, mailSubject; TextEdit mailBody;
    string mailPath; bool mailLoaded, mailAsked;
    string[] mailPendingNames; string mailPendingSubject, mailPendingText;

    string MailPath => Path.GetFullPath(ProjectSettings.GlobalizePath("res://../userdata/mail_" + (client.CharacterName ?? "x").Replace('/', '_') + ".json"));

    void BuildMailUi()
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4);
        mailList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 110) };
        mailList.ItemSelected += i => ShowMail((int)i);
        mailHead = new Label { Text = "", Modulate = new Color(1, 1, 0.8f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        mailText = new RichTextLabel { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 110), BbcodeEnabled = false, SelectionEnabled = true };
        mailText.AddThemeFontSizeOverride("normal_font_size", 13);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
        Button b(string t, string tip, Action a) { var x = new Button { Text = t, ClipText = true, TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; x.Pressed += a; row.AddChild(x); return x; }
        b("New", "Write a new letter", () => ComposeMail("", ""));
        b("Reply", "Answer the selected letter", () => { var m = SelectedMail(); if (m != null) ComposeMail(m.Sender, m.Title.StartsWith("Re:") ? m.Title : "Re: " + m.Title); });
        b("Delete", "Delete the selected letter", DeleteSelectedMail);
        b("Check", "Ask the server for new mail", () => client.RequestMail());
        box.AddChild(mailList); box.AddChild(mailHead); box.AddChild(mailText); box.AddChild(row);
        mailWin = hudMgr.Add("mail", "Mail", box, new Vector2(0.22f, 0.10f), new Vector2(380, 420), visibleByDefault: false);
        mailWin.Visible = false;

        var nb = new VBoxContainer(); nb.AddThemeConstantOverride("separation", 4);
        nb.AddChild(new Label { Text = "To (names separated by commas):" });
        mailTo = new LineEdit { PlaceholderText = "player name" }; nb.AddChild(mailTo);
        nb.AddChild(new Label { Text = "Subject:" });
        mailSubject = new LineEdit { MaxLength = 40 }; nb.AddChild(mailSubject);
        mailBody = new TextEdit { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 140), WrapMode = TextEdit.LineWrappingMode.Boundary };
        nb.AddChild(mailBody);
        var nrow = new HBoxContainer(); nrow.AddThemeConstantOverride("separation", 4);
        var send = new Button { Text = "Send", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; send.Pressed += SendMailPressed;
        var cancel = new Button { Text = "Cancel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cancel.Pressed += () => mailNewWin.Visible = false;
        nrow.AddChild(send); nrow.AddChild(cancel); nb.AddChild(nrow);
        mailNewWin = hudMgr.Add("mail_new", "Write a letter", nb, new Vector2(0.30f, 0.20f), new Vector2(380, 380), visibleByDefault: false);
        mailNewWin.Visible = false;

        client.MailReceived += OnMailReceived;
        client.NamesResolved += OnNamesResolved;
        client.ChatReceived += (text, type) => { if (type == "System" || type == "Message") { if (text != null && text.Contains("new mail")) client.RequestMail(); } };
    }

    /// <summary>The top bar button; opens the window and fetches new mail.</summary>
    void ToggleMail()
    {
        LoadMail();
        hudMgr.Toggle("mail");
        if (mailWin.Visible) { client.RequestMail(); FillMail(); }
    }

    /// <summary>Once per login: read our saved mail and ask the server for new letters.</summary>
    void MailRoomReady()
    {
        if (mailAsked) return;
        mailAsked = true; LoadMail(); client.RequestMail();
        var mt = System.Environment.GetEnvironmentVariable("M59_MAILTEST");   // test hook: "to|subject|text" is sent a few seconds after login
        if (mt != null) { var p = mt.Split('|'); var tt = new Godot.Timer { OneShot = true, WaitTime = 5 }; AddChild(tt); tt.Timeout += () => { GD.Print("[mail] test: sending to " + p[0]); mailPendingNames = new[] { p[0] }; mailPendingSubject = p[1]; mailPendingText = p[2]; client.LookupNames(mailPendingNames); tt.QueueFree(); }; tt.Start(); }
    }

    void LoadMail()
    {
        string path = MailPath;
        if (mailLoaded && path == mailPath) return;
        mailLoaded = true; mailPath = path; mails.Clear();
        try { if (File.Exists(path)) mails.AddRange(JsonSerializer.Deserialize<List<MailEntry>>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true }) ?? new List<MailEntry>()); }
        catch (Exception ex) { GD.Print("[mail] cannot read " + path + ": " + ex.Message); }
        UpdateMailButton();
    }

    void SaveMail()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(mailPath)); File.WriteAllText(mailPath, JsonSerializer.Serialize(mails, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true })); }
        catch (Exception ex) { GD.Print("[mail] cannot save: " + ex.Message); }
    }

    void OnMailReceived(MailItem m)
    {
        LoadMail();
        uint next = mails.Count == 0 ? 1 : mails.Max(x => x.Num) + 1;
        mails.Add(new MailEntry { Num = next, Sender = m.Sender, Title = m.Title, Text = m.Text, Time = m.Time, Recipients = m.Recipients });
        SaveMail(); FillMail(); UpdateMailButton();
        AddChatLine($"New mail from {m.Sender}: {m.Title}", "System");
    }

    static string MailDate(uint t) => t == 0 ? "" : DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    void FillMail()
    {
        if (mailList == null) return;
        mailList.Clear();
        foreach (var m in mails.OrderByDescending(x => x.Time).ThenByDescending(x => x.Num))
        {
            int i = mailList.AddItem((m.Read ? "" : "* ") + m.Sender + "  -  " + (m.Title.Length == 0 ? "(no subject)" : m.Title));
            mailList.SetItemMetadata(i, (long)m.Num);
        }
        mailHead.Text = mails.Count == 0 ? "No letters." : ""; mailText.Text = "";
    }

    MailEntry SelectedMail()
    {
        var s = mailList.GetSelectedItems();
        if (s.Length == 0) return null;
        uint num = (uint)(long)mailList.GetItemMetadata(s[0]);
        return mails.FirstOrDefault(x => x.Num == num);
    }

    void ShowMail(int idx)
    {
        var m = SelectedMail(); if (m == null) return;
        mailHead.Text = $"From {m.Sender}\nTo {string.Join(", ", m.Recipients)}\n{MailDate(m.Time)}\n{m.Title}";
        mailText.Text = m.Text;
        if (!m.Read) { m.Read = true; SaveMail(); mailList.SetItemText(idx, m.Sender + "  -  " + (m.Title.Length == 0 ? "(no subject)" : m.Title)); UpdateMailButton(); }
    }

    void DeleteSelectedMail()
    {
        var m = SelectedMail(); if (m == null) return;
        mails.Remove(m); SaveMail(); FillMail(); UpdateMailButton();
    }

    void UpdateMailButton()
    {
        if (mailBtn == null) return;
        int unread = mails.Count(x => !x.Read);
        mailBtn.TooltipText = unread > 0 ? $"Mail ({unread} unread)" : "Mail";
        mailBtn.Modulate = unread > 0 ? new Color(1f, 1f, 0.5f) : Colors.White;
    }

    void ComposeMail(string to, string subject)
    {
        LoadMail();
        mailTo.Text = to; mailSubject.Text = subject; mailBody.Text = "";
        mailNewWin.Visible = true; mailNewWin.GetParent().MoveChild(mailNewWin, -1);
        (to.Length == 0 ? (Control)mailTo : mailBody).GrabFocus();
    }

    void SendMailPressed()
    {
        var names = mailTo.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) { AddChatLine("Who should get this letter?", "System"); return; }
        if (names.Length > 20) { AddChatLine("A letter can go to at most 20 players.", "System"); return; }
        mailPendingNames = names; mailPendingSubject = mailSubject.Text; mailPendingText = mailBody.Text;
        client.LookupNames(names);
    }

    void OnNamesResolved(uint[] ids)
    {
        if (mailPendingNames == null) return;
        var names = mailPendingNames; mailPendingNames = null;
        if (ids.Length != names.Length) { AddChatLine("The letter could not be addressed.", "System"); return; }
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] == 0) { AddChatLine($"There is no player called {names[i]}.", "System"); return; }
        client.SendMail(ids, mailPendingSubject, mailPendingText);
        mailNewWin.Visible = false;
        AddChatLine("Your letter has been sent to " + string.Join(", ", names) + ".", "System");
    }

    // ---------------------------------------------------------------- guild
    VBoxContainer guildBody; HudWindow guildWin; GuildInfo guildInfo; ItemList guildMembers; uint guildCreateNormal, guildCreateSecret; bool guildAsk, guildNone;
    static readonly string[] DefaultMaleRanks = { "Initiate", "Member", "Veteran", "Lieutenant", "Guildmaster" };
    static readonly string[] DefaultFemaleRanks = { "Initiate", "Member", "Veteran", "Lieutenant", "Guildmistress" };

    void BuildGuildUi()
    {
        guildBody = new VBoxContainer(); guildBody.AddThemeConstantOverride("separation", 4);
        var scroll = new ScrollContainer { ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; guildBody.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(guildBody);
        guildWin = hudMgr.Add("guild", "Guild", scroll, new Vector2(0.56f, 0.10f), new Vector2(340, 420), visibleByDefault: false);
        guildWin.Visible = false;
        client.GuildInfoReceived += gi => { guildInfo = gi; guildAsk = false; guildNone = false; DrawGuild(); };
        client.ChatReceived += (text, type) => { if (text != null && text.Contains("do not belong to a guild")) { guildInfo = null; guildAsk = false; guildNone = true; DrawGuild(); } };
        client.GuildAskReceived += (normal, secret) => { guildInfo = null; guildNone = false; guildAsk = true; guildCreateNormal = normal; guildCreateSecret = secret; DrawGuild();
            var gt = System.Environment.GetEnvironmentVariable("M59_GUILDTEST");   // test hook: found a guild with this name at once
            if (gt != null) { GD.Print("[guild] test: founding " + gt); client.GuildCreate(gt, DefaultMaleRanks, DefaultFemaleRanks, false); var tt = new Godot.Timer { OneShot = true, WaitTime = 1.5 }; AddChild(tt); tt.Timeout += () => { client.GuildRequest(); tt.QueueFree(); }; tt.Start(); } if (!guildWin.Visible) hudMgr.Toggle("guild"); };
    }

    void ToggleGuild()
    {
        hudMgr.Toggle("guild");
        if (guildWin.Visible) client.GuildRequest();
    }

    void ClearGuildBody() { foreach (var c in guildBody.GetChildren()) { guildBody.RemoveChild(c); c.QueueFree(); } }

    void DrawGuild()
    {
        ClearGuildBody(); guildMembers = null;
        if (guildAsk) { DrawGuildCreate(); return; }
        if (guildNone && guildInfo == null) { guildBody.AddChild(new Label { Text = "You do not belong to a guild.", Modulate = new Color(1, 0.9f, 0.5f) }); guildBody.AddChild(new Label { Text = "Guilds are founded at a guild hall registrar. Once you are invited, accept and your guild shows up here.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) }); return; }
        if (guildInfo == null) { guildBody.AddChild(new Label { Text = "Asking the server ...", Modulate = new Color(1, 1, 1, 0.6f) }); return; }
        var gi = guildInfo;
        var members = gi.GuildMembers.ToList();
        var me = members.FirstOrDefault(m => m.ID == client.PlayerId);
        string[] male = { gi.Rank1Male, gi.Rank2Male, gi.Rank3Male, gi.Rank4Male, gi.Rank5Male }, female = { gi.Rank1Female, gi.Rank2Female, gi.Rank3Female, gi.Rank4Female, gi.Rank5Female };
        string RankName(GuildMemberEntry m) => m.Rank >= 1 && m.Rank <= 5 ? (m.Gender == Meridian59.Common.Enums.Gender.Female ? female : male)[m.Rank - 1] : "";
        var title = new Label { Text = gi.GuildName, Modulate = new Color(1, 0.9f, 0.5f) }; title.AddThemeFontSizeOverride("font_size", 18);
        guildBody.AddChild(title);
        guildBody.AddChild(new Label { Text = me != null ? "You are " + RankName(me) + "." : "", Modulate = new Color(1, 1, 1, 0.7f) });
        guildMembers = new ItemList { CustomMinimumSize = new Vector2(0, 200), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var m in members.OrderByDescending(x => x.Rank).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            int i = guildMembers.AddItem($"{m.Name}  ({RankName(m)})");
            guildMembers.SetItemMetadata(i, (long)m.ID);
        }
        guildBody.AddChild(new Label { Text = $"Members: {members.Count}" });
        guildBody.AddChild(guildMembers);
        uint Selected() { var s = guildMembers.GetSelectedItems(); return s.Length == 0 ? 0 : (uint)(long)guildMembers.GetItemMetadata(s[0]); }
        var fl = gi.Flags;
        var row1 = new HBoxContainer(); row1.AddThemeConstantOverride("separation", 4);
        Button add(HBoxContainer row, string t, string tip, Action a, bool enabled = true) { var x = new Button { Text = t, ClipText = true, TooltipText = tip, Disabled = !enabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; x.Pressed += a; row.AddChild(x); return x; }
        add(row1, "Invite target", "Invite the player you have targeted into the guild", () => { if (targetId != 0 && targetId != client.PlayerId) client.GuildInvite(targetId); else AddChatLine("Click a player first, then press Invite target.", "System"); }, fl.IsInvite);
        add(row1, "Exile", "Throw the selected member out of the guild", () => { var s = Selected(); if (s != 0 && s != client.PlayerId) { client.GuildExile(s); client.GuildRequest(); } }, fl.IsExile);
        add(row1, "Refresh", "Ask the server again", () => client.GuildRequest());
        guildBody.AddChild(row1);
        var row2 = new HBoxContainer(); row2.AddThemeConstantOverride("separation", 4);
        add(row2, "Leave guild", "Leave this guild", () => ConfirmGuild("Leave " + gi.GuildName + "?", () => { client.GuildRenounce(); guildInfo = null; client.GuildRequest(); }), fl.IsRenounce);
        add(row2, "Hand over", "Make the selected member the leader of the guild", () => { var s = Selected(); if (s != 0 && s != client.PlayerId) ConfirmGuild("Hand the guild over to the selected member?", () => { client.GuildAbdicate(s); client.GuildRequest(); }); }, fl.IsAbdicate);
        add(row2, "Disband", "Close the guild for good", () => ConfirmGuild("Disband " + gi.GuildName + " for good?", () => { client.GuildDisband(); guildInfo = null; client.GuildRequest(); }), fl.IsDisband);
        guildBody.AddChild(row2);
    }

    HudWindow guildConfirmWin; Label guildConfirmText; Action guildConfirmYes;
    void ConfirmGuild(string question, Action yes)
    {
        if (guildConfirmWin == null)
        {
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8);
            guildConfirmText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            box.AddChild(guildConfirmText);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
            var ok = new Button { Text = "Yes", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var no = new Button { Text = "No", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            ok.Pressed += () => { guildConfirmWin.Visible = false; var a = guildConfirmYes; guildConfirmYes = null; a?.Invoke(); };
            no.Pressed += () => { guildConfirmWin.Visible = false; guildConfirmYes = null; };
            row.AddChild(ok); row.AddChild(no); box.AddChild(row);
            guildConfirmWin = hudMgr.Add("guild_confirm", "Are you sure?", box, new Vector2(0.38f, 0.30f), new Vector2(300, 120), resizable: false, visibleByDefault: false);
        }
        guildConfirmText.Text = question; guildConfirmYes = yes;
        guildConfirmWin.Visible = true; guildConfirmWin.GetParent().MoveChild(guildConfirmWin, -1);
    }

    void DrawGuildCreate()
    {
        guildBody.AddChild(new Label { Text = "You are not in a guild.", Modulate = new Color(1, 0.9f, 0.5f) });
        guildBody.AddChild(new Label { Text = $"Founding a guild costs {guildCreateNormal} shillings ({guildCreateSecret} for a secret guild). Choose a name and the titles of the five ranks.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.7f) });
        var name = new LineEdit { PlaceholderText = "Guild name", MaxLength = 30 };
        guildBody.AddChild(name);
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddChild(new Label { Text = "Men:" }); grid.AddChild(new Label { Text = "Women:" });
        var m = new LineEdit[5]; var f = new LineEdit[5];
        for (int i = 4; i >= 0; i--)
        {
            m[i] = new LineEdit { Text = DefaultMaleRanks[i], MaxLength = 20, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            f[i] = new LineEdit { Text = DefaultFemaleRanks[i], MaxLength = 20, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            grid.AddChild(m[i]); grid.AddChild(f[i]);
        }
        guildBody.AddChild(grid);
        var secret = new CheckBox { Text = "Secret guild" };
        guildBody.AddChild(secret);
        var create = new Button { Text = "Found the guild" };
        create.Pressed += () =>
        {
            if (name.Text.Trim().Length == 0) { AddChatLine("Your guild needs a name.", "System"); return; }
            client.GuildCreate(name.Text.Trim(), m.Select(x => x.Text).ToArray(), f.Select(x => x.Text).ToArray(), secret.ButtonPressed);
            guildAsk = false; guildInfo = null; DrawGuild();
            var t = new Godot.Timer { OneShot = true, WaitTime = 1.5 }; AddChild(t); t.Timeout += () => { client.GuildRequest(); t.QueueFree(); }; t.Start();
        };
        guildBody.AddChild(create);
    }

    void BuildSocialUi()
    {
        BuildWhoUi(); BuildMailUi(); BuildGuildUi();
    }
}
