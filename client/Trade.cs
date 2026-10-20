using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Meridian59.Data.Models;

/// <summary>Shops, selling, banking and player-to-player trading (official buy.c / offer.c / gameuser.c).
/// Buy / withdraw: ask the seller (BP_REQ_BUY) -> a list with prices arrives -> pick items and amounts -> BP_REQ_BUY_ITEMS.
/// Offer / deposit: pick a recipient close by, then items from the inventory -> BP_REQ_OFFER.  The receiver answers with a counteroffer, the
/// offerer accepts or cancels (shops answer with money automatically).</summary>
public partial class Main
{
    const float TradeDistance = 5 * 1024f;     // official CLOSE_DISTANCE
    const int MaxPurchase = 500;

    string tradeOtherName = "";                // who we offer to (shown in the offer window)
    bool tradeDepositing;                      // the item picker in progress is a deposit, not an offer
    List<ObjectBase> tradeOfferItems = new List<ObjectBase>(), tradeCounterItems = new List<ObjectBase>();
    bool tradeSendOpen, tradeRecvOpen, tradeGotCounter, tradeCounterSent;

    void BuildTradeUi()
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        box.AddChild(new Label { Text = "Stand close to a shop keeper, banker or another player.", Modulate = new Color(1, 1, 1, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        void add(string text, string tip, Action act) { var b = new Button { Text = text, TooltipText = tip, ClipText = true }; b.Pressed += act; box.AddChild(b); }
        add("Buy", "Buy from a shop keeper close by", TradeBuy);
        add("Offer / sell", "Offer items to a player or a shop keeper close by", () => TradeOffer(false));
        add("Deposit", "Give items to a banker close by", () => TradeOffer(true));
        add("Withdraw", "Take items out of your bank account", TradeWithdraw);
        hudMgr.Add("trade", "Trade", box, new Vector2(0.20f, 0.14f), new Vector2(260, 220), resizable: false, visibleByDefault: false).Visible = false;

        client.BuyListReceived += OnBuyList;
        client.OfferReceived += OnOfferReceived;
        client.OfferedReceived += OnOffered;
        client.CounterOfferReceived += OnCounterOffer;
        client.CounterOfferedReceived += OnCounterOffered;
        client.OfferCanceledReceived += OnOfferCanceled;
    }

    // ---- helpers
    HudWindow TradeWindow(string id, string title, Vector2 pos, Vector2 size, out VBoxContainer body)
    {
        var existing = hudMgr.Get(id);
        if (existing != null)
        {
            body = (VBoxContainer)((ScrollContainer)existing.Content).GetChild(0);
            existing.SetTitle(title);
            foreach (var c in body.GetChildren()) { body.RemoveChild(c); c.QueueFree(); }
            return existing;
        }
        body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 4);
        var sc = new ScrollContainer { ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        sc.AddChild(body);
        var w = hudMgr.Add(id, title, sc, pos, size, visibleByDefault: false);
        w.Visible = false;
        return w;
    }

    void ShowTradeWindow(HudWindow w) { w.Visible = true; w.GetParent().MoveChild(w, -1); }
    void HideTradeWindow(string id) { var w = hudMgr.Get(id); if (w != null) w.Visible = false; }

    string NameOf(ObjectBase o) => client.Resolve(o.NameRID) ?? "?";

    /// <summary>Close-by objects (within 5 squares) that pass the filter, nearest first.</summary>
    List<RoomObject> NearbyObjects(Func<RoomObject, bool> pass)
    {
        var res = new List<(float d, RoomObject o)>();
        if (walker == null) return new List<RoomObject>();
        foreach (var o in client.Objects)
        {
            if (o.ID == client.PlayerId || !pass(o) || !objPos.TryGetValue(o.ID, out var p)) continue;
            float d = Mathf.Sqrt((p.x - walker.X) * (p.x - walker.X) + (p.y - walker.Y) * (p.y - walker.Y));
            if (d <= TradeDistance) res.Add((d, o));
        }
        return res.OrderBy(x => x.d).Select(x => x.o).ToList();
    }

    /// <summary>Choose one of the close-by objects (several: a small list; one: at once).</summary>
    void PickNearby(string title, string none, Func<RoomObject, bool> pass, Action<RoomObject> chosen)
    {
        var list = NearbyObjects(pass);
        if (list.Count == 0) { AddChatLine(none, "System"); return; }
        if (list.Count == 1) { chosen(list[0]); return; }
        var w = TradeWindow("trade_pick", title, new Vector2(0.40f, 0.25f), new Vector2(280, 60 + list.Count * 34), out var body);
        foreach (var o in list)
        {
            var obj = o;
            var b = new Button { Text = client.Resolve(obj.NameRID) ?? "?", ClipText = true };
            b.Pressed += () => { HideTradeWindow("trade_pick"); chosen(obj); };
            body.AddChild(b);
        }
        var cancel = new Button { Text = "Cancel" }; cancel.Pressed += () => HideTradeWindow("trade_pick");
        body.AddChild(cancel);
        ShowTradeWindow(w);
    }

    /// <summary>One line: [check] icon name [price] [amount].  Returns the row's check box / amount so the caller can read the selection.</summary>
    Control ItemLine(ObjectBase it, bool selectable, out CheckBox check, out SpinBox amount, uint price = 0, bool showPrice = false, Action changed = null, int maxAmount = 0)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        check = null; amount = null;
        if (selectable)
        {
            check = new CheckBox { CustomMinimumSize = new Vector2(28, 0) };
            var on = new StyleBoxFlat { BgColor = new Color(0.25f, 0.55f, 0.25f, 0.55f) };
            check.AddThemeStyleboxOverride("hover", on); check.AddThemeStyleboxOverride("pressed", on); check.AddThemeStyleboxOverride("hover_pressed", on); check.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            check.TooltipText = "Tick to select";
            row.AddChild(check);
        }
        var icon = new TextureRect { Texture = IconFor(it), CustomMinimumSize = new Vector2(34, 34), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        row.AddChild(icon);
        string name = NameOf(it);
        var label = new Label { Text = it.IsStackable && !selectable ? $"{name}  x{it.Count}" : name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(label);
        if (showPrice) row.AddChild(new Label { Text = price.ToString(), Modulate = new Color(1f, 0.85f, 0.3f), VerticalAlignment = VerticalAlignment.Center });
        if (selectable && it.IsStackable)
        {
            int max = maxAmount > 0 ? maxAmount : (int)Math.Max(1, it.Count);
            amount = new SpinBox { MinValue = 1, MaxValue = max, Value = Math.Min(max, Math.Max(1, (int)it.Count)), CustomMinimumSize = new Vector2(80, 0) };
            amount.ValueChanged += _ => changed?.Invoke();
            row.AddChild(amount);
        }
        if (check != null) check.Toggled += _ => changed?.Invoke();
        return row;
    }

    static ObjectID Pick(ObjectBase it, SpinBox amount) => new ObjectID(it.ID, it.IsStackable ? (uint)(amount?.Value ?? it.Count) : 0);

    // ---- buy / withdraw
    void TradeBuy() => PickNearby("Buy from", "There is nobody close enough to buy from.", o => o.Flags.IsBuyable, o => client.TradeRequestBuy(o.ID));
    void TradeWithdraw() => PickNearby("Withdraw from", "There is no banker close enough.", o => o.Flags.IsBuyable, o => client.TradeRequestWithdraw(o.ID));

    void OnBuyList(ObjectBase seller, TradeOfferObject[] items, bool withdrawal)
    {
        var w = TradeWindow("trade_buy", (withdrawal ? "Withdraw from " : "Buy from ") + NameOf(seller), new Vector2(0.30f, 0.15f), new Vector2(430, 380), out var body);
        var rows = new List<(TradeOfferObject it, CheckBox ck, SpinBox amt)>();
        var total = new Label { Text = "Total: 0" };
        void update()
        {
            long sum = 0;
            foreach (var r in rows) if (r.ck.ButtonPressed) sum += (long)r.it.Price * (r.amt != null ? (long)r.amt.Value : 1);
            total.Text = withdrawal ? "Selected items: " + rows.Count(r => r.ck.ButtonPressed) : "Total: " + sum;
        }
        body.AddChild(new Label { Text = items.Length == 0 ? "Nothing for sale." : (withdrawal ? "Tick the items to take out." : "Tick the items to buy (price each)."), Modulate = new Color(1, 1, 1, 0.7f) });
        foreach (var it in items)
        {
            body.AddChild(ItemLine(it, true, out var ck, out var amt, it.Price, !withdrawal || it.Price > 0, update, MaxPurchase));
            rows.Add((it, ck, amt));
        }
        body.AddChild(total);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8);
        var ok = new Button { Text = withdrawal ? "Withdraw" : "Buy", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ok.Pressed += () =>
        {
            var pick = rows.Where(r => r.ck.ButtonPressed).Select(r => Pick(r.it, r.amt)).ToArray();
            if (pick.Length > 0) { if (withdrawal) client.TradeWithdrawItems(seller.ID, pick); else client.TradeBuyItems(seller.ID, pick); }
            HideTradeWindow("trade_buy");
        };
        var cancel = new Button { Text = "Cancel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cancel.Pressed += () => HideTradeWindow("trade_buy");
        buttons.AddChild(ok); buttons.AddChild(cancel); body.AddChild(buttons);
        ShowTradeWindow(w);
        if (System.Environment.GetEnvironmentVariable("M59_TRADEAUTO") == "1" && rows.Count > 0) { rows[0].ck.ButtonPressed = true; GD.Print("[trade] auto: buying the first item"); ok.EmitSignal(BaseButton.SignalName.Pressed); }
    }

    // ---- offer / deposit: pick the recipient, then the items
    void TradeOffer(bool deposit)
    {
        if (tradeSendOpen || tradeRecvOpen) { var open = hudMgr.Get(tradeSendOpen ? "trade_send" : "trade_recv"); if (open != null) ShowTradeWindow(open); return; }
        if (client.Inventory.Count == 0) { AddChatLine("You have nothing to offer.", "System"); return; }
        PickNearby(deposit ? "Deposit with" : "Offer to", "There is nobody close enough.", o => o.Flags.IsOfferable, o =>
        {
            tradeOtherName = client.Resolve(o.NameRID) ?? "?";
            PickItems(deposit ? "Deposit which items?" : "Offer which items?", items =>
            {
                if (items.Length == 0) return;
                if (deposit) client.TradeDeposit(o.ID, items); else client.TradeOffer(o.ID, items);
            });
        });
    }

    /// <summary>Inventory item picker (check boxes + amounts for stacks).</summary>
    void PickItems(string title, Action<ObjectID[]> done)
    {
        var w = TradeWindow("trade_items", title, new Vector2(0.32f, 0.12f), new Vector2(400, 380), out var body);
        var rows = new List<(ObjectBase it, CheckBox ck, SpinBox amt)>();
        foreach (var it in client.Inventory)
        {
            body.AddChild(ItemLine(it, true, out var ck, out var amt));
            rows.Add((it, ck, amt));
        }
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8);
        var ok = new Button { Text = "OK", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ok.Pressed += () => { var pick = rows.Where(r => r.ck.ButtonPressed).Select(r => Pick(r.it, r.amt)).ToArray(); HideTradeWindow("trade_items"); done(pick); };
        var cancel = new Button { Text = "Cancel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cancel.Pressed += () => HideTradeWindow("trade_items");
        buttons.AddChild(ok); buttons.AddChild(cancel); body.AddChild(buttons);
        ShowTradeWindow(w);
        if (System.Environment.GetEnvironmentVariable("M59_TRADEAUTO") == "1" && rows.Count > 0 && title.StartsWith("Offer")) { rows[0].ck.ButtonPressed = true; GD.Print("[trade] auto: offering the first item"); ok.EmitSignal(BaseButton.SignalName.Pressed); }
    }

    // ---- the offer windows
    void OfferLists(VBoxContainer body, string yourTitle, List<ObjectBase> yours, string theirTitle, List<ObjectBase> theirs, string theirEmpty)
    {
        body.AddChild(new Label { Text = yourTitle, Modulate = new Color(0.7f, 0.9f, 1f) });
        if (yours.Count == 0) body.AddChild(new Label { Text = "(nothing)", Modulate = new Color(1, 1, 1, 0.6f) });
        foreach (var it in yours) body.AddChild(ItemLine(it, false, out _, out _));
        body.AddChild(new HSeparator());
        body.AddChild(new Label { Text = theirTitle, Modulate = new Color(0.7f, 1f, 0.7f) });
        if (theirs == null) body.AddChild(new Label { Text = theirEmpty, Modulate = new Color(1, 1, 1, 0.6f) });
        else if (theirs.Count == 0) body.AddChild(new Label { Text = "(nothing)", Modulate = new Color(1, 1, 1, 0.6f) });
        else foreach (var it in theirs) body.AddChild(ItemLine(it, false, out _, out _));
    }

    // we made an offer: the server confirms it with BP_OFFERED
    void OnOffered(ObjectBase[] items)
    {
        tradeOfferItems = items.ToList(); tradeSendOpen = true; tradeGotCounter = false;
        DrawSendOffer();
    }
    void DrawSendOffer()
    {
        var w = TradeWindow("trade_send", "Offer to " + tradeOtherName, new Vector2(0.32f, 0.15f), new Vector2(400, 380), out var body);
        OfferLists(body, "You offer:", tradeOfferItems, "You receive:", tradeGotCounter ? tradeCounterItems : null, "(waiting for an answer)");
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8);
        var accept = new Button { Text = "Accept", Disabled = !tradeGotCounter, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        accept.Pressed += () => { client.TradeAccept(); CloseOfferWindows(); };
        var cancel = new Button { Text = "Cancel offer", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cancel.Pressed += () => { client.TradeCancel(); CloseOfferWindows(); };
        buttons.AddChild(accept); buttons.AddChild(cancel); body.AddChild(buttons);
        ShowTradeWindow(w);
    }
    void OnCounterOffer(ObjectBase[] items)
    {
        if (!tradeSendOpen) return;
        tradeCounterItems = items.ToList(); tradeGotCounter = true;
        DrawSendOffer();
        AddChatLine("You have an answer to your offer: check the offer window, then accept or cancel.", "System");
    }

    // someone offers us something
    void OnOfferReceived(ObjectBase from, ObjectBase[] items)
    {
        if (tradeSendOpen || tradeRecvOpen) return;     // one offer at a time (like the original)
        tradeOtherName = NameOf(from); tradeOfferItems = items.ToList(); tradeCounterItems = new List<ObjectBase>(); tradeRecvOpen = true; tradeCounterSent = false;
        DrawReceiveOffer();
        AddChatLine($"{tradeOtherName} makes you an offer.", "System");
    }
    void DrawReceiveOffer()
    {
        var w = TradeWindow("trade_recv", tradeOtherName + " offers you", new Vector2(0.32f, 0.15f), new Vector2(400, 400), out var body);
        OfferLists(body, tradeOtherName + " offers:", tradeOfferItems, "You give in return:", tradeCounterSent ? tradeCounterItems : null, "(nothing yet)");
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8);
        var accept = new Button { Text = "Accept (give nothing)", Disabled = tradeCounterSent, ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Take the offered items without giving anything back" };
        accept.Pressed += () => { client.TradeCounter(new ObjectID[0]); tradeCounterSent = true; DrawReceiveOffer(); };
        var set = new Button { Text = "Give items...", Disabled = tradeCounterSent, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Choose what you give in return" };
        set.Pressed += () => PickItems("Give which items?", items => { client.TradeCounter(items); tradeCounterSent = true; DrawReceiveOffer(); });
        var cancel = new Button { Text = "Cancel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cancel.Pressed += () => { client.TradeCancel(); CloseOfferWindows(); };
        buttons.AddChild(accept); buttons.AddChild(set); buttons.AddChild(cancel); body.AddChild(buttons);
        if (tradeCounterSent) body.AddChild(new Label { Text = "Waiting for " + tradeOtherName + " to decide...", Modulate = new Color(1, 1, 0.6f) });
        ShowTradeWindow(w);
    }
    void OnCounterOffered(ObjectBase[] items)
    {
        if (!tradeRecvOpen) return;
        tradeCounterItems = items.ToList(); tradeCounterSent = true;
        DrawReceiveOffer();
    }

    void OnOfferCanceled()
    {
        if (tradeSendOpen || tradeRecvOpen) AddChatLine("The offer is over.", "System");
        CloseOfferWindows();
    }
    void CloseOfferWindows()
    {
        tradeSendOpen = tradeRecvOpen = false; tradeGotCounter = tradeCounterSent = false;
        HideTradeWindow("trade_send"); HideTradeWindow("trade_recv"); HideTradeWindow("trade_items"); HideTradeWindow("trade_pick");
    }

    double tradeTestAt; object tradeTestRoom;
    /// <summary>Test hook: M59_TRADETEST=buy|offer|withdraw opens that trade flow a few seconds after entering a room.</summary>
    void TradeTestTick()
    {
        var t = System.Environment.GetEnvironmentVariable("M59_TRADETEST");
        if (t == null || !roomShown || walker == null) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (!ReferenceEquals(tradeTestRoom, currentRoom)) { tradeTestRoom = currentRoom; tradeTestAt = now + 5; }
        if (tradeTestAt < 0 || now < tradeTestAt) return;
        tradeTestAt = System.Environment.GetEnvironmentVariable("M59_TRADELOOP") == "1" ? now + 12 : -1; GD.Print("[trade] test " + t);
        foreach (var o in client.Objects) if (objPos.TryGetValue(o.ID, out var op)) GD.Print($"[trade]   {NameOf(o)} #{o.ID} buyable={o.Flags.IsBuyable} offerable={o.Flags.IsOfferable} dist={Mathf.Sqrt((op.x - walker.X) * (op.x - walker.X) + (op.y - walker.Y) * (op.y - walker.Y)) / 1024f:F1} squares");
        switch (t) { case "buy": TradeBuy(); break; case "offer": TradeOffer(false); break; case "withdraw": TradeWithdraw(); break; }
    }
}
