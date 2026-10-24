using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace DamageMeter;

/// <summary>
/// A live damage meter: each player's damage per second and total, broken down by spell.
/// The host counts (enemy health is removed on the host) and shares the numbers with everyone who has the mod.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "girthy_mods.DamageMeter";
    public const string Name = "DamageMeter";
    public const string Version = "1.1.0";

    internal static new ManualLogSource Log;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> Visible;
    internal static ConfigEntry<bool> ShowTab;
    internal static ConfigEntry<bool> Detailed;
    internal static ConfigEntry<bool> ShowWhenSolo;
    internal static ConfigEntry<bool> KeepAfterRun;
    internal static ConfigEntry<int> DpsWindowSeconds;
    internal static ConfigEntry<int> MaxSpellsPerPlayer;
    internal static ConfigEntry<string> ToggleKey;
    internal static ConfigEntry<string> DetailKey;
    internal static ConfigEntry<string> ResetKey;
    internal static ConfigEntry<string> Anchor;
    internal static ConfigEntry<float> OffsetX;
    internal static ConfigEntry<float> OffsetY;
    internal static ConfigEntry<float> Scale;
    internal static ConfigEntry<float> BackgroundOpacity;

    public override void Load()
    {
        Log = base.Log;

        Enabled = Config.Bind("General", "Enabled", true, "Count damage and show the meter.");
        DpsWindowSeconds = Config.Bind("General", "DpsWindowSeconds", 10,
            new ConfigDescription("DPS is the damage of the last this-many seconds (game time, so shops and pauses don't count).", new AcceptableValueRange<int>(1, 60)));

        Visible = Config.Bind("Display", "Visible", true, "Show the meter (the toggle key flips this).");
        ShowTab = Config.Bind("Display", "ShowTabWhenHidden", true, "When the meter is hidden, leave a small DAMAGE tab you can click to bring it back.");
        Detailed = Config.Bind("Display", "ShowSpells", true, "Show each player's damage per spell (the detail key flips this).");
        ShowWhenSolo = Config.Bind("Display", "ShowWhenSolo", true, "Show the meter in single player too.");
        KeepAfterRun = Config.Bind("Display", "KeepAfterRun", true, "Keep showing the last run's numbers after it ends, until the next run starts.");
        MaxSpellsPerPlayer = Config.Bind("Display", "MaxSpellsPerPlayer", 8,
            new ConfigDescription("Spell lines per player in the detailed view; the rest are summed as \"other\".", new AcceptableValueRange<int>(1, 20)));
        Anchor = Config.Bind("Display", "Position", "Left",
            new ConfigDescription("Screen edge or corner for the meter.",
                new AcceptableValueList<string>("Left", "Right", "TopLeft", "TopRight", "BottomLeft", "BottomRight")));
        OffsetX = Config.Bind("Display", "OffsetX", 20f, "Distance from the left/right edge (at 1080p).");
        OffsetY = Config.Bind("Display", "OffsetY", 60f, "Distance from the top/bottom edge, or up from the middle for Left/Right (at 1080p).");
        Scale = Config.Bind("Display", "Scale", 1f, new ConfigDescription("Size of the meter.", new AcceptableValueRange<float>(0.5f, 2.5f)));
        BackgroundOpacity = Config.Bind("Display", "BackgroundOpacity", 0.7f, new ConfigDescription("0 = no background.", new AcceptableValueRange<float>(0f, 1f)));

        ToggleKey = Config.Bind("Keys", "ToggleKey", "F3", "Show or hide the meter (Unity Input System key name).");
        DetailKey = Config.Bind("Keys", "SpellsKey", "F4", "Switch the per-spell lines on or off.");
        ResetKey = Config.Bind("Keys", "ResetKey", "", "Optional key to reset the numbers mid-run (host). Empty = no key.");

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Tracker));
        AddComponent<Overlay>();
        Log.LogInfo($"{Name} {Version} ready.");
    }
}
