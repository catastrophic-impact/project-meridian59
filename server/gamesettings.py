"""Game settings shown in the server admin TUI ("Game settings" tab).  Each one is a property of the Kod `Settings` object (kod/util/settings.kod).
kind: choice (value -> label), bool, int.  The admin TUI reads them with `show object <id>` and writes them with `set object <id> <prop> int <value>`,
then saves the game so they survive restarts."""

DEATH_RULE = {0: "Never", 1: "Only killed by a player (PvP)", 2: "Only killed by a monster (PvE)", 3: "Always"}

# (category, property, label, kind, choices/None, description)
SETTINGS = [
    ("Death", "piDeathItemDrop", "Items drop on death", "choice", DEATH_RULE,
     "Whose kills make the dead player drop their belongings onto the corpse."),
    ("Death", "piDeathStatLoss", "Lose max HP / skills on death", "choice", DEATH_RULE,
     "Whose kills cost the dead player max health and skill/spell percents (the original 'death cost')."),
    ("Death", "pbDeathStartAtOne", "Wake up at 1 HP / 1 mana / 1 stamina", "bool", None,
     "After death the player starts in the underworld with 1 health, 1 mana and 1 vigor and must rest."),
    ("Reagents", "piReagentMode", "Reagents", "choice",
     {0: "Mandatory (original - no reagents = no spell)", 1: "Not needed at all (no penalty)", 2: "Optional: weaker without them"},
     "How spell reagents are treated."),
    ("Reagents", "piNoReagentPercent", "Effectiveness without reagents", "choice",
     {10: "10% (very weak)", 25: "25%", 50: "50% (default)", 75: "75% (mild)"},
     "Optional mode. Damage, healing and durations are this percent of normal; casting time and the mana cost of instant utility spells go up by (100 - percent)%. 75 = 75% damage/duration, 25% longer casting, 25% more mana."),
    ("Gameplay", "pbSpellKarmaRequired", "Spells need karma", "bool", None,
     "Off (default): Qor / Shalille karma requirements of spells are ignored. The Amulet of the Three and the Sword of the Hunt keep their own karma rules either way."),
    ("Groups", "piMaxGroupSize", "Maximum group size", "int", None, "Players and companion bots together (default 5)."),
    ("Groups", "pbAllowCompanionBots", "Allow companion bots", "bool", None, "Players may call temporary companion bots (mage, healer, tank) that join their group."),
    ("Groups", "piMaxCompanionsPerPlayer", "Companions per player", "int", None, "How many companions one player may have at the same time."),
    ("Logoff", "piLogoffGhostMode", "Logoff ghost", "choice",
     {0: "Off (no ghost, no consequences)", 1: "Dies: character is sent to the underworld when the time runs out", 2: "Normal (original penalties)"},
     "What happens to a player who logs off in an unsafe place. Time limit is piLogoffPenaltyGhostTime (default 10 minutes)."),
    ("Gameplay", "pbUnlimitedEating", "Unlimited eating", "bool", None, "Players can always eat and drink, however full they are."),
    ("Gameplay", "piBowNoAmmoDamagePercent", "Bow without arrows: damage %", "int", None,
     "A bow with no arrows equipped shoots for this percent of a plain arrow's damage (default 50). Equipped arrows do their full damage."),
    ("Gameplay", "pbBowAutoEquipAmmo", "Bow auto-equips arrows", "bool", None, "Original behaviour: arrows in the pack are equipped automatically when firing."),
    ("Gameplay", "pbBowUsesAmmo", "Bow uses up arrows", "bool", None, "Firing with arrows equipped uses one up (original)."),
    ("Gameplay", "piElevatorAutoCycleSec", "Brax arena auto-solve (seconds)", "int", None, "The Brax arena pillar puzzle (room G9, platform lift + undead horde) solves itself this often when nobody is working on it; 0 = off. It stays solved 2 minutes."),
    ("Gameplay", "piMobAggroMode", "Mob aggro mode", "choice", {0: "Original (one chaser limits the others)", 1: "Proximity (WoW / DAoC style)"}, "1 = WoW / DAoC style: every aggressive monster that a player comes close to (see radius) goes for them, any number at once. 0 = original: a monster already chasing someone talks the others out of it."),
    ("Gameplay", "piMobAggroRadius", "Mob aggro radius (squares)", "int", None, "Proximity mode: how close a player must come before an aggressive monster notices them."),
    ("Gameplay", "pbCompanionDebug", "Companion debug log", "bool", None, "Writes what each companion bot decides (distance to its owner, stuck checks, state) to the server debug log (server/channel/debug-*.txt). For testing."),
    ("Economy", "piMoneyFactor", "Money drop factor %", "int", None, "Raise to give out more money (100 = original)."),
    ("Economy", "piItemFactor", "Monster item drop factor %", "int", None, "Raise to drop more items from monsters (100 = original)."),
    ("Economy", "piMagicItemModifier", "Magic item chance %", "int", None, "Modifier to the chance for magic items to be generated."),
    ("Economy", "piSpawnRate", "Monster spawn rate %", "int", None, "Lower = monsters respawn faster (100 = original)."),
    ("Progress", "piAdvancementRate", "Skill / spell advancement %", "int", None, "Percent of normal advancement speed."),
    ("Progress", "piHPGainMultiplier", "Health gain multiplier", "int", None, "Multiplier for the level-up chance each kill adds."),
    ("Progress", "pbAtrophyOn", "Skill atrophy", "bool", None, "Unused skills slowly decay."),
]

def fmt_value(kind, choices, raw):
    if raw is None:
        return "?"
    if kind == "choice":
        return f"{raw} = {choices.get(raw, '?')}"
    if kind == "bool":
        return "On" if raw else "Off"
    return str(raw)
