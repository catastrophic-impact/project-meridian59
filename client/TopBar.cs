using Godot;

/// <summary>Contents of the top bar: rest/stand toggle (original client icons), window buttons, interface + menu.</summary>
public partial class Main
{
    int restFullTicks;
    TextureButton restBtn; AtlasTexture restStand, restSit;

    static Texture2D LoadUiTexture(string name)
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://ui/" + name));
        return img == null ? null : ImageTexture.CreateFromImage(img);
    }

    /// <summary>Two-frame sheet (normal | pressed) -> two textures.</summary>
    static (Texture2D a, Texture2D b) Frames(string name, int w, int h)
    {
        var t = LoadUiTexture(name);
        if (t == null) return (null, null);
        return (new AtlasTexture { Atlas = t, Region = new Rect2(0, 0, w, h) }, new AtlasTexture { Atlas = t, Region = new Rect2(w, 0, w, h) });
    }

    void BuildTopBarButtons()
    {
        if (hudMgr == null) return;
        // rest / stand: the original stand.bmp has the standing figure on the left and the sitting figure on the right
        var sheet = LoadUiTexture("stand.png");
        if (sheet != null)
        {
            restStand = new AtlasTexture { Atlas = sheet, Region = new Rect2(0, 0, 36, 36) };
            restSit = new AtlasTexture { Atlas = sheet, Region = new Rect2(36, 0, 36, 36) };
            restBtn = hudMgr.AddBarIcon(restStand, restSit, "Rest (R)", new Vector2(34, 34), () => SetResting(!resting));
        }
        else hudMgr.AddBarButton("Rest", "Rest (R)", () => SetResting(!resting));
        hudMgr.AddBarSeparator();

        void icon(string file, int w, int h, string tip, System.Action act)
        {
            var (n, p) = Frames(file, w, h);
            if (n != null) hudMgr.AddBarIcon(n, p, tip, new Vector2(w * 1.5f, h * 1.5f), act);
        }
        icon("inventory.png", 32, 20, "Inventory (I)", () => ToggleTab(0));
        icon("spells.png", 32, 20, "Spells (B)", () => ToggleTab(1));
        icon("skills.png", 32, 20, "Skills (K)", () => ToggleTab(2));
        var (mn, mp) = Frames("mail.png", 24, 20);
        if (mn != null) mailBtn = hudMgr.AddBarIcon(mn, mp, "Mail", new Vector2(36, 30), () => ToggleMail());
        else hudMgr.AddBarButton("Mail", "Read and write letters", () => ToggleMail());
        hudMgr.AddBarButton("Who", "Players who are online", () => ToggleWho());
        hudMgr.AddBarButton("Guild", "Your guild: members, invite, leave", () => ToggleGuild());
        hudMgr.AddBarSeparator();
        hudMgr.AddBarButton("Character", "Show / hide the character window", () => hudMgr.Toggle("status"));
        hudMgr.AddBarButton("Map", "Show / hide the map", () => hudMgr.Toggle("map"));
        hudMgr.AddBarButton("Chat", "Show / hide the chat window", () => hudMgr.Toggle("chat"));
        hudMgr.AddBarButton("Hotbar", "Show / hide the hotbar", () => hudMgr.Toggle("hotbar"));
        hudMgr.AddBarButton("Bots", "Companion bots: call, dismiss, give orders", () => hudMgr.Toggle("companions"));
        hudMgr.AddBarButton("Group", "Your group: members, invite, leave", () => hudMgr.Toggle("group"));

        hudMgr.AddBarButton("Interface", "Window lock, UI scale, volume (F10)", () => hudMgr.Toggle("interface"), right: true);
        hudMgr.AddBarButton("Menu", "Game menu (Esc)", () => ToggleGameMenu(), right: true);
    }

    /// <summary>Rest (sit) or stand; used by the R key and the top bar button.</summary>
    /// <summary>True when health and mana are at their maximum and vigor has reached its rest threshold (the server's rest timer stops adding vigor at the threshold).</summary>
    bool FullyRested()
    {
        bool full(byte num, bool vigor)
        {
            var st = client.GetNumeric(Meridian59.Common.Enums.StatGroup.Condition, num);
            if (st == null) return true;
            return st.ValueCurrent >= st.ValueMaximum;      // vigor: ValueMaximum carries the rest threshold
        }
        return full(1, false) && full(2, false) && full(3, true);
    }

    void SetResting(bool rest)
    {
        if (client == null || client.State != GameClient.Phase.Playing) { UpdateRestButton(); return; }
        if (rest == resting) return;
        if (rest && FullyRested()) { AddChatLine("You are already fully rested.", "System"); return; }
        restFullTicks = 0;
        resting = rest;
        if (resting) client.Rest(); else client.Stand();
        AddChatLine(resting ? "You sit down to rest." : "You stand up.", "System");
        UpdateRestButton();
    }

    /// <summary>Called ~4x per second: stand up by ourselves once everything is recovered (resting longer gains nothing).</summary>
    void CheckAutoStand()
    {
        if (!resting) { restFullTicks = 0; return; }
        if (!FullyRested()) { restFullTicks = 0; return; }
        if (++restFullTicks >= 4) { AddChatLine("You are fully rested.", "System"); SetResting(false); }
    }

    void UpdateRestButton()
    {
        if (restBtn == null) return;
        restBtn.TextureNormal = resting ? restSit : restStand;
        restBtn.TextureHover = restBtn.TextureNormal;
        restBtn.TexturePressed = resting ? restStand : restSit;
        restBtn.TooltipText = resting ? "Stand up (R)" : "Rest (R)";
    }
}
