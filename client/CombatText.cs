using System.Collections.Generic;
using Godot;

/// <summary>Floating combat numbers and combat lines in the chat. Fed by our server extension (damage / heals with amounts); only events that involve the player or
/// the group (companions) are shown: damage we or our companions do (yellow / white), damage the player takes (red) and our companions take (orange), heals (green).</summary>
public partial class Main
{
    class FloatText { public Label3D L; public Label H; public float Age, Life = 1.3f; public Vector3 Start; public Color Col; }
    readonly List<FloatText> floatTexts = new List<FloatText>();
    CanvasLayer floatLayer;

    bool InOurGroup(uint id) => id == client.PlayerId || client.Group.Exists(m => m.Id == id);

    string NameOf(uint id)
    {
        if (id == client.PlayerId) return "you";
        var m = client.Group.Find(g => g.Id == id);
        if (m != null) return m.Name;
        var o = client.Objects.Find(x => x.ID == id);
        return o != null ? (client.Resolve(o.NameRID) ?? "something") : "something";
    }

    void OnCombatNumber(uint victim, uint attacker, int amount, byte kind)
    {
        if (!roomShown) return;
        bool heal = kind == 1;
        bool ours = InOurGroup(victim) || (!heal && InOurGroup(attacker));
        if (!ours) return;
        // colour: heal green, we / our bots hit a monster yellow, the player is hit red, a companion is hit orange
        Color col = heal ? new Color(0.35f, 1f, 0.4f)
                  : victim == client.PlayerId ? new Color(1f, 0.25f, 0.2f)
                  : InOurGroup(victim) ? new Color(1f, 0.6f, 0.2f)
                  : new Color(1f, 0.95f, 0.45f);
        // chat line
        string who = NameOf(victim), by = NameOf(attacker);
        string line;
        if (heal) line = victim == client.PlayerId ? $"You are healed for {amount}." : $"{who} is healed for {amount}.";
        else if (attacker == client.PlayerId) line = $"You hit {who} for {amount}.";
        else if (victim == client.PlayerId) line = $"{Cap(by)} hits you for {amount}.";
        else line = $"{Cap(by)} hits {who} for {amount}.";
        AddChatLine(line, "System");
        // floating number
        string text = (heal ? "+" : "-") + amount;
        if (victim == client.PlayerId && !thirdPerson) SpawnHudFloat(text, col);
        else SpawnWorldFloat(victim == client.PlayerId ? client.PlayerId : victim, text, col);
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    void SpawnWorldFloat(uint id, string text, Color col)
    {
        if (!objSprites.TryGetValue(id, out var sp) || !IsInstanceValid(sp)) return;
        var lbl = sp.GetNodeOrNull<Label3D>("name");
        float top = lbl != null ? lbl.Position.Y : (sp.Texture != null ? sp.Texture.GetHeight() * sp.PixelSize : 1.6f);
        float jitter = (GD.Randf() - 0.5f) * 0.4f;
        var l = new Label3D { Text = text, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, NoDepthTest = true, PixelSize = 0.007f, FontSize = 54, OutlineSize = 12, Modulate = col, OutlineModulate = new Color(0, 0, 0, 0.95f), RenderPriority = 10 };
        objectsRoot.AddChild(l);
        var start = sp.Position + new Vector3(jitter, top + 0.1f, 0);
        l.Position = start;
        floatTexts.Add(new FloatText { L = l, Start = start, Col = col });
    }

    void SpawnHudFloat(string text, Color col)
    {
        if (floatLayer == null) { floatLayer = new CanvasLayer { Layer = 4 }; AddChild(floatLayer); }
        var vs = GetViewport().GetVisibleRect().Size;
        var h = new Label { Text = text, Modulate = col, MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeFontSizeOverride("font_size", 34);
        h.AddThemeColorOverride("font_outline_color", Colors.Black); h.AddThemeConstantOverride("outline_size", 8);
        h.Position = new Vector2(vs.X * 0.5f - 20 + (GD.Randf() - 0.5f) * 120, vs.Y * 0.55f);
        floatLayer.AddChild(h);
        floatTexts.Add(new FloatText { H = h, Col = col, Life = 1.2f, Start = new Vector3(h.Position.X, h.Position.Y, 0) });
    }

    double combatTestAt;
    void UpdateFloatTexts(float dt)
    {
        if (System.Environment.GetEnvironmentVariable("M59_COMBATTEST") == "1" && roomShown)
        {
            double t = Time.GetTicksMsec() / 1000.0;
            if (combatTestAt == 0) combatTestAt = t + 6;
            else if (combatTestAt > 0 && t > combatTestAt)
            {
                combatTestAt = t + 0.35;
                var comp = client.Group.Find(g => g.Kind == 2);
                OnCombatNumber(comp != null ? comp.Id : client.PlayerId, client.PlayerId, 7, 0);
                OnCombatNumber(client.PlayerId, 0, 3, 1);
                if (t > 8) combatTestAt = -1;
            }
        }

        for (int i = floatTexts.Count - 1; i >= 0; i--)
        {
            var f = floatTexts[i];
            f.Age += dt;
            float k = f.Age / f.Life;
            var c = new Color(f.Col.R, f.Col.G, f.Col.B, Mathf.Clamp(1.4f - k * 1.2f, 0f, 1f));
            if (f.L != null) { if (!IsInstanceValid(f.L)) { floatTexts.RemoveAt(i); continue; } f.L.Position = f.Start + new Vector3(0, k * 0.8f, 0); f.L.Modulate = c; }
            if (f.H != null) { if (!IsInstanceValid(f.H)) { floatTexts.RemoveAt(i); continue; } f.H.Position = new Vector2(f.Start.X, f.Start.Y - k * 70f); f.H.Modulate = c; }
            if (f.Age >= f.Life) { (f.L as Node)?.QueueFree(); (f.H as Node)?.QueueFree(); floatTexts.RemoveAt(i); }
        }
    }

    void ClearFloatTexts() { foreach (var f in floatTexts) { if (f.L != null && IsInstanceValid(f.L)) f.L.QueueFree(); if (f.H != null && IsInstanceValid(f.H)) f.H.QueueFree(); } floatTexts.Clear(); }
}
