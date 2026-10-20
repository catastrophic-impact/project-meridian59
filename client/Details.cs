using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The "Details" window (top bar "Stats"): the numbers the server keeps behind the character, asked for every 1.5 s while the window is open
/// (our server only, see user.kod SendStatsDetail). Offense / defense and the hit chances against the current target are the server's real formulas.</summary>
public partial class Main
{
    HudWindow detailsWin; VBoxContainer detailsBox; double detailsAt; List<(byte id, int a, int b)> detailsRows = new List<(byte, int, int)>(); uint detailsTarget;

    static readonly (uint bit, string name)[] WeaponTypes =
    {
        (0x1, "all weapon attacks"), (0x2, "non-magical weapons"), (0x4, "magical weapons"), (0x8, "bare hands"), (0x10, "bludgeoning"), (0x20, "piercing"), (0x40, "thrusting"), (0x80, "slashing"),
        (0x100, "whips"), (0x200, "claws"), (0x400, "bites"), (0x800, "stings"), (0x1000, "acid"), (0x2000, "unarmed"), (0x4000, "punches"), (0x8000, "kicks"), (0x10000, "nerudite"), (0x20000, "silver"),
    };
    static readonly (uint bit, string name)[] SpellTypes =
    {
        (0x1, "all spells"), (0x2, "fire"), (0x4, "shock"), (0x8, "cold"), (0x10, "holy"), (0x20, "unholy"), (0x40, "acid"), (0x80, "quake"), (0x100, "Hunter's sword"),
    };
    static readonly string[] DetailAttrNames = { "", "Might", "Intellect", "Agility", "Aim", "Stamina", "Mysticism" };

    static string TypeName(int type)
    {
        uint bits = (uint)Math.Abs(type);
        var table = type >= 0 ? WeaponTypes : SpellTypes;
        var names = table.Where(t => (bits & t.bit) != 0).Select(t => t.name).ToList();
        return names.Count > 0 ? string.Join(", ", names) : "type " + type;
    }

    void BuildDetailsUi()
    {
        var scroll = new ScrollContainer { ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        detailsBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; detailsBox.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(detailsBox);
        detailsWin = hudMgr.Add("details", "Details", scroll, new Vector2(0.01f, 0.30f), new Vector2(330, 470), visibleByDefault: false);
        detailsWin.Visible = false;
        client.DetailsReceived += rows => { detailsRows = rows; UpdateGrowthBar(); if (AnimLog) GD.Print("[details] " + string.Join(" ", rows.Select(r => $"{r.id}:{r.a}/{r.b}"))); FillDetails(); };
        client.EquipmentChanged += () => { if (detailsWin.Visible) detailsAt = 0; };
    }

    void ToggleDetails()
    {
        hudMgr.Toggle("details");
        if (detailsWin.Visible) detailsAt = 0;
    }

    /// <summary>The thin bar under vigor: how close the hidden "gain chance" counter is to its high mark, i.e. the chance that the next kill of a tougher creature adds a point of maximum health.</summary>
    void UpdateGrowthBar()
    {
        if (growthBar == null) return;
        foreach (var r in detailsRows)
            if (r.id == 50)
            {
                double pct = r.b > 0 ? Math.Clamp(r.a * 100.0 / r.b, 0, 100) : 0;
                growthBar.Value = pct / 100.0;
                growthBar.TooltipText = $"Growth: {pct:F0}% chance that your next kill of a creature tougher than you makes you hardier (+1 maximum health).\nKilling creatures weaker than you sets the progress back; stamina helps it along.";
            }
    }

    void PollDetails()
    {
        if (detailsWin == null || !roomShown) return;
        bool open = detailsWin.Visible;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < detailsAt && (!open || targetId == detailsTarget)) return;
        detailsAt = now + (open ? 1.5 : 5); detailsTarget = targetId;
        client.RequestDetails(targetId);
    }

    void FillDetails()
    {
        if (detailsBox == null) return;
        foreach (var c in detailsBox.GetChildren()) { detailsBox.RemoveChild(c); c.QueueFree(); }
        var rows = detailsRows;
        (int a, int b)? Row(byte id) { foreach (var r in rows) if (r.id == id) return (r.a, r.b); return null; }
        var good = new Color(0.55f, 1f, 0.55f); var bad = new Color(1f, 0.55f, 0.5f);
        GridContainer grid = null;
        void Section(string title)
        {
            var l = new Label { Text = title, Modulate = new Color(1f, 0.9f, 0.5f) }; l.AddThemeFontSizeOverride("font_size", 15);
            detailsBox.AddChild(new HSeparator()); detailsBox.AddChild(l);
            grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 1);
            detailsBox.AddChild(grid);
        }
        void Line(string label, string value, Color? color = null, string tip = null)
        {
            var l = new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip ?? "", MouseFilter = tip != null ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore };
            var v = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore };
            if (color != null) v.Modulate = color.Value;
            grid.AddChild(l); grid.AddChild(v);
        }
        string Signed(int n) => n > 0 ? "+" + n : n.ToString();
        string target = targetId != 0 && targetId != client.PlayerId ? (client.Resolve(client.Objects.Find(o => o.ID == targetId)?.NameRID ?? 0) ?? "your target") : null;

        if (rows.Count == 0) { Section("Details"); Line("Waiting for the server ...", ""); return; }

        Section("Attributes");
        for (byte id = 1; id <= 6; id++)
            if (Row(id) is var (a, b))
                Line(DetailAttrNames[id], b != a ? $"{a}  (base {b}, {Signed(a - b)})" : a.ToString(), a > b ? good : a < b ? bad : null, "Effects such as Bless change the current value; the base is what the character has by itself.");
        if (Row(7) is var (k, _)) Line("Karma", k.ToString());

        Section("Vitals");
        if (Row(10) is var (h, hm)) Line("Health", $"{h} / {hm}");
        if (Row(13) is var (mh, bh)) Line("Maximum health", mh != bh ? $"{mh}  (base {bh}, {Signed(mh - bh)})" : mh.ToString(), mh > bh ? good : mh < bh ? bad : null);
        if (Row(11) is var (m, mm)) Line("Mana", $"{m} / {mm}");
        if (Row(12) is var (vg, vm)) Line("Vigor", $"{vg}  (rest to {vm})");
        if (Row(50) is var (gc, gh)) Line("Growth", gh > 0 ? $"{Math.Clamp(gc * 100.0 / gh, 0, 100):F0}% chance" : "-", null, "The chance that your next kill of a creature tougher than you adds a point of maximum health. It builds up with every such fight.");
        if (Row(14) is var (bk, bm)) Line("Bulk carried", $"{bk} / {bm}");
        if (Row(15) is var (wt, wm)) Line("Weight carried", $"{wt} / {wm}");

        Section("Offense");
        Line("Fighting with", AttackName() ?? "bare hands");
        if (Row(21) is var (st, _)) Line("Weapon skill", st.ToString(), null, "How well you swing this kind of weapon (the stroke skill).");
        if (Row(22) is var (pf, _)) Line("Proficiency", pf.ToString(), null, "How practised you are with this weapon.");
        if (Row(20) is var (off, oe)) Line("Offense rating", oe != 0 ? $"{off}  ({Signed(oe)} from gear and effects)" : off.ToString(), oe > 0 ? good : oe < 0 ? bad : null, "Skill, proficiency, aim and health, plus weapon, effects and the universal bonus. The higher against the target's defense, the more you hit.");
        if (Row(36) is var (um, _) && um != 0) Line("Universal bonus", Signed(um) + "%", um > 0 ? good : bad, "Adds to offense, damage and defense.");
        if (Row(24) is var (hc, _)) Line(target != null ? $"Chance to hit {target}" : "Chance to hit", hc + "%", null, "Your offense against its defense (between 10 and 95 percent).");
        else Line("Chance to hit", "click a target to see", new Color(1, 1, 1, 0.5f));
        if (Row(25) is var (td, _)) Line("Its defense", td.ToString());

        Section("Defense");
        if (Row(30) is var (df, de)) Line("Defense rating", de != 0 ? $"{df}  ({Signed(de)} from gear and effects)" : df.ToString(), de > 0 ? good : de < 0 ? bad : null, "Parry, block, dodge, agility and health, plus armor, effects and the universal bonus.");
        if (Row(31) is var (pa, _)) Line("Parry", pa.ToString());
        if (Row(32) is var (bl, _)) Line("Block", bl.ToString());
        if (Row(33) is var (dg, _)) Line("Dodge", dg.ToString());
        if (Row(34) is var (th, _)) Line(target != null ? $"Chance {target} hits you" : "Chance it hits you", th + "%", th > 60 ? bad : null);
        if (Row(35) is var (to, _)) Line("Its offense", to.ToString());

        var res = rows.Where(r => r.id == 40).ToList();
        Section("Resistances");
        if (res.Count == 0) Line("None", "");
        foreach (var r in res) Line(TypeName(r.a), r.b > 0 ? $"{r.b}% resisted" : $"{-r.b}% weaker", r.b > 0 ? good : bad);
    }
}
