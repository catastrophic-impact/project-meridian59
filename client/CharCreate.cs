using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Meridian59.Data.Models;

/// <summary>Character creation screen (shown when the account's character slot has not been created yet).
/// Rules come from the server (system.kod SendCharInfo / player.kod PlayerNewCharInfo): each attribute 1..50, sum &lt;= 200,
/// spells + skills cost points (10 / 25) with a budget of 45 (the server treats more as a hacked character and grants nothing), faces/colours only from the offered lists.</summary>
public partial class Main
{
    const int AttrMax = 50, AttrSum = 200, PointBudget = 45;
    static readonly string[] AttrNames = { "Might", "Intellect", "Stamina", "Agility", "Mysticism", "Aim" };

    Control createPanel; CharCreationInfo ccInfo;
    LineEdit ccName; Label ccStatus, ccAttrLeft, ccPointsLeft; TextureRect ccPic;
    bool ccFemale; int ccHair, ccEyes, ccNose, ccMouth, ccHairCol, ccSkinCol;
    readonly SpinBox[] ccAttr = new SpinBox[6];
    readonly List<(CheckBox box, uint id, int cost)> ccSpells = new List<(CheckBox, uint, int)>();
    readonly List<(CheckBox box, uint id, int cost)> ccSkills = new List<(CheckBox, uint, int)>();
    readonly List<Label> ccPartLabels = new List<Label>();
    Button ccCreateBtn;
    bool ccBuilding;

    void HideCharCreate() { if (createPanel != null) { createPanel.GetParent()?.QueueFree(); createPanel = null; } }

    void ShowCharCreate(CharCreationInfo ci)
    {
        HideCharCreate();
        ccInfo = ci; ccFemale = false; ccHair = ccEyes = ccNose = ccMouth = ccHairCol = ccSkinCol = 0;
        ccSpells.Clear(); ccSkills.Clear(); ccPartLabels.Clear();
        if (loginPanel != null) loginPanel.Visible = false;

        var layer = new CanvasLayer { Layer = 20 };
        var bg = new ColorRect { Color = new Color(0.04f, 0.04f, 0.07f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 10);
        var title = new Label { Text = "Create your character", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26);
        root.AddChild(title);

        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 18);

        // ---- column 1: appearance
        var c1 = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; c1.AddThemeConstantOverride("separation", 6);
        var picHolder = new PanelContainer { CustomMinimumSize = new Vector2(150, 160), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        picHolder.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.11f, 1f), BorderColor = new Color(0.35f, 0.35f, 0.45f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        ccPic = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        picHolder.AddChild(ccPic); c1.AddChild(picHolder);
        ccName = new LineEdit { PlaceholderText = "character name (3-30 letters)", MaxLength = 30 };
        c1.AddChild(new Label { Text = "Name" }); c1.AddChild(ccName);
        var gender = new HBoxContainer();
        var bMale = new Button { Text = "Male", ToggleMode = true, ButtonPressed = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var bFemale = new Button { Text = "Female", ToggleMode = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bMale.Pressed += () => { ccFemale = false; bMale.ButtonPressed = true; bFemale.ButtonPressed = false; ccHair = ccEyes = ccNose = ccMouth = 0; RefreshCreateLooks(); };
        bFemale.Pressed += () => { ccFemale = true; bFemale.ButtonPressed = true; bMale.ButtonPressed = false; ccHair = ccEyes = ccNose = ccMouth = 0; RefreshCreateLooks(); };
        gender.AddChild(bMale); gender.AddChild(bFemale);
        c1.AddChild(gender);
        c1.AddChild(CreateStepper("Hair", () => HairIds().Length, d => ccHair = Wrap(ccHair + d, HairIds().Length)));
        c1.AddChild(CreateStepper("Eyes", () => EyeIds().Length, d => ccEyes = Wrap(ccEyes + d, EyeIds().Length)));
        c1.AddChild(CreateStepper("Nose", () => NoseIds().Length, d => ccNose = Wrap(ccNose + d, NoseIds().Length)));
        c1.AddChild(CreateStepper("Mouth", () => MouthIds().Length, d => ccMouth = Wrap(ccMouth + d, MouthIds().Length)));
        c1.AddChild(CreateStepper("Hair colour", () => ci.HairColors.Length, d => ccHairCol = Wrap(ccHairCol + d, ci.HairColors.Length)));
        c1.AddChild(CreateStepper("Skin colour", () => ci.SkinColors.Length, d => ccSkinCol = Wrap(ccSkinCol + d, ci.SkinColors.Length)));
        cols.AddChild(c1);

        // ---- column 2: attributes
        var c2 = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) }; c2.AddThemeConstantOverride("separation", 6);
        c2.AddChild(new Label { Text = "Attributes (each 1-50)" });
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = AttrNames[i], CustomMinimumSize = new Vector2(100, 0) });
            var sp = new SpinBox { MinValue = 1, MaxValue = AttrMax, Step = 1, Value = 25, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            sp.ValueChanged += v => OnAttrChanged(idx);
            ccAttr[i] = sp; row.AddChild(sp); c2.AddChild(row);
        }
        ccAttrLeft = new Label(); c2.AddChild(ccAttrLeft);
        c2.AddChild(new Label { Text = "Presets" });
        var presets = new HBoxContainer();
        foreach (var (txt, act) in new (string, Action)[] { ("Mage", ci.SetAttributesToMage), ("Warrior", ci.SetAttributesToWarrior), ("Hybrid", ci.SetAttributesToHybrid) })
        {
            var a = act;
            var b = new Button { Text = txt, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.Pressed += () => { a(); ccBuilding = true; var v = ci.GetAttributesArray(); for (int k = 0; k < 6; k++) ccAttr[k].Value = v[k]; ccBuilding = false; UpdateCreatePoints(); };
            presets.AddChild(b);
        }
        c2.AddChild(presets);
        cols.AddChild(c2);

        // ---- column 3: spells and skills
        var c3 = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) }; c3.AddThemeConstantOverride("separation", 6);
        ccPointsLeft = new Label(); c3.AddChild(ccPointsLeft);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 250) };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddChild(new Label { Text = "Spells" });
        foreach (var sp in ci.Spells.OrderBy(s => (byte)s.SchoolType).ThenBy(s => s.SpellCost))
        {
            string nm = client.Resolve(sp.SpellNameID) ?? ("spell " + sp.ExtraID);
            string desc = client.Resolve(sp.SpellDescriptionID) ?? "";
            var cb = new CheckBox { Text = $"{nm}  ({SchoolNames[Math.Min((int)(byte)sp.SchoolType, 7)]}, {sp.SpellCost} pts)", TooltipText = desc };
            cb.Toggled += on => { if (!on) { UpdateCreatePoints(); return; } if (SpentPoints() > PointBudget) { cb.SetPressedNoSignal(false); } UpdateCreatePoints(); };
            ccSpells.Add((cb, sp.ExtraID, (int)sp.SpellCost)); list.AddChild(cb);
        }
        list.AddChild(new Label { Text = "Skills" });
        foreach (var sk in ci.Skills.OrderBy(s => s.SkillCost))
        {
            string nm = client.Resolve(sk.SkillNameID) ?? ("skill " + sk.ExtraID);
            string desc = client.Resolve(sk.SkillDescriptionID) ?? "";
            var cb = new CheckBox { Text = $"{nm}  ({sk.SkillCost} pts)", TooltipText = desc };
            cb.Toggled += on => { if (on && SpentPoints() > PointBudget) cb.SetPressedNoSignal(false); UpdateCreatePoints(); };
            ccSkills.Add((cb, sk.ExtraID, (int)sk.SkillCost)); list.AddChild(cb);
        }
        scroll.AddChild(list); c3.AddChild(scroll);
        cols.AddChild(c3);
        root.AddChild(cols);

        ccStatus = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(ccStatus);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; buttons.AddThemeConstantOverride("separation", 16);
        ccCreateBtn = new Button { Text = "Create character", CustomMinimumSize = new Vector2(200, 36) };
        var cancel = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(120, 36) };
        ccCreateBtn.Pressed += SubmitCharacter;
        cancel.Pressed += () => ReturnToLogin("Character creation cancelled.");
        buttons.AddChild(ccCreateBtn); buttons.AddChild(cancel); root.AddChild(buttons);

        margin.AddChild(root); center.AddChild(margin);
        layer.AddChild(bg); layer.AddChild(center); AddChild(layer);
        createPanel = center;

        ccBuilding = true;
        ci.SetAttributesToHybrid();
        var v0 = ci.GetAttributesArray(); for (int k = 0; k < 6; k++) ccAttr[k].Value = v0[k];
        ccBuilding = false;
        UpdateCreatePoints(); RefreshCreateLooks();
        if (shotPath != null && shotCountdown < 0 && optCreateName == null) shotCountdown = 30;
        GD.Print($"[create] options: {ci.Spells.Count} spells, {ci.Skills.Count} skills, hair male {ci.MaleHairIDs.Length} female {ci.FemaleHairIDs.Length}, hair colours {ci.HairColors.Length}, skin colours {ci.SkinColors.Length}");
    }

    static int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;
    ResourceIDBGF[] HairIds() => ccFemale ? ccInfo.FemaleHairIDs : ccInfo.MaleHairIDs;
    ResourceIDBGF[] EyeIds() => ccFemale ? ccInfo.FemaleEyeIDs : ccInfo.MaleEyeIDs;
    ResourceIDBGF[] NoseIds() => ccFemale ? ccInfo.FemaleNoseIDs : ccInfo.MaleNoseIDs;
    ResourceIDBGF[] MouthIds() => ccFemale ? ccInfo.FemaleMouthIDs : ccInfo.MaleMouthIDs;
    uint SkullId() => (ccFemale ? ccInfo.FemaleSkullID : ccInfo.MaleSkullID).Value;

    /// <summary>"Label  &lt;  n/total  &gt;" row; step(+-1) changes the choice, then the portrait is redrawn.</summary>
    Control CreateStepper(string text, Func<int> count, Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(100, 0) });
        var l = new Button { Text = "<", CustomMinimumSize = new Vector2(34, 0) };
        var val = new Label { HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var r = new Button { Text = ">", CustomMinimumSize = new Vector2(34, 0) };
        l.Pressed += () => { step(-1); RefreshCreateLooks(); };
        r.Pressed += () => { step(1); RefreshCreateLooks(); };
        row.AddChild(l); row.AddChild(val); row.AddChild(r);
        ccPartLabels.Add(val);
        return row;
    }

    void RefreshCreateLooks()
    {
        if (ccInfo == null) return;
        int[] cur = { ccHair, ccEyes, ccNose, ccMouth, ccHairCol, ccSkinCol };
        int[] tot = { HairIds().Length, EyeIds().Length, NoseIds().Length, MouthIds().Length, ccInfo.HairColors.Length, ccInfo.SkinColors.Length };
        for (int i = 0; i < ccPartLabels.Count && i < 6; i++) ccPartLabels[i].Text = $"{(tot[i] == 0 ? 0 : cur[i] + 1)} / {tot[i]}";
        var ro = new RoomObject();
        byte skin = ccInfo.SkinColors.Length > 0 ? ccInfo.SkinColors[ccSkinCol] : (byte)0;
        byte hair = ccInfo.HairColors.Length > 0 ? ccInfo.HairColors[ccHairCol] : (byte)0;
        void add(uint rid, byte hs, byte col) { if (rid != 0) ro.SubOverlays.Add(new SubOverlay(rid, null, hs, col, 0)); }
        add(SkullId(), 1, skin);
        if (EyeIds().Length > 0) add(EyeIds()[ccEyes].Value, 11, skin);
        if (MouthIds().Length > 0) add(MouthIds()[ccMouth].Value, 12, skin);
        if (HairIds().Length > 0) add(HairIds()[ccHair].Value, 13, hair);
        if (NoseIds().Length > 0) add(NoseIds()[ccNose].Value, 14, skin);
        ccPic.Texture = FaceComposer.Compose(ro, rid => ResolveFile(client.Resolve(rid)));
    }

    void OnAttrChanged(int idx)
    {
        if (ccBuilding) return;
        int sum = 0; foreach (var s in ccAttr) sum += (int)s.Value;
        if (sum > AttrSum) { ccBuilding = true; ccAttr[idx].Value -= sum - AttrSum; ccBuilding = false; }
        UpdateCreatePoints();
    }

    int SpentPoints() => ccSpells.Where(s => s.box.ButtonPressed).Sum(s => s.cost) + ccSkills.Where(s => s.box.ButtonPressed).Sum(s => s.cost);

    void UpdateCreatePoints()
    {
        int sum = 0; foreach (var s in ccAttr) sum += (int)s.Value;
        if (ccAttrLeft != null) ccAttrLeft.Text = $"Attribute points: {sum} / {AttrSum}";
        if (ccPointsLeft != null) ccPointsLeft.Text = $"Spell + skill points left: {PointBudget - SpentPoints()} of {PointBudget}";
    }

    void SubmitCharacter()
    {
        string name = ccName.Text.Trim();
        if (name.Length < 3) { ccStatus.Text = "Please enter a name (3-30 characters)."; return; }
        if (HairIds().Length == 0 || EyeIds().Length == 0 || NoseIds().Length == 0 || MouthIds().Length == 0) { ccStatus.Text = "The server offered no face parts."; return; }
        var ids = new uint[] { SkullId(), HairIds()[ccHair].Value, EyeIds()[ccEyes].Value, NoseIds()[ccNose].Value, MouthIds()[ccMouth].Value };
        var attrs = ccAttr.Select(s => (uint)s.Value).ToArray();
        var spells = ccSpells.Where(s => s.box.ButtonPressed).Select(s => s.id).ToArray();
        var skills = ccSkills.Where(s => s.box.ButtonPressed).Select(s => s.id).ToArray();
        byte hair = ccInfo.HairColors.Length > 0 ? ccInfo.HairColors[ccHairCol] : (byte)0;
        byte skin = ccInfo.SkinColors.Length > 0 ? ccInfo.SkinColors[ccSkinCol] : (byte)0;
        ccStatus.Text = "Creating...";
        ccCreateBtn.Disabled = true;
        GD.Print($"[create] sending '{name}' female={ccFemale} attrs={string.Join(",", attrs)} spells={spells.Length} skills={skills.Length}");
        client.CreateCharacter(name, ccFemale, ids, hair, skin, attrs, spells, skills);
    }

    void OnCharCreationRejected()
    {
        if (ccStatus != null) ccStatus.Text = "The server refused that name (already taken or not allowed). Try another.";
        if (ccCreateBtn != null) ccCreateBtn.Disabled = false;
    }
}
