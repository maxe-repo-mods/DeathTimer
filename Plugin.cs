using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace DeathTimer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    private const string PluginGuid = "maxenterme.DeathTimer";
    private const string PluginName = "DeathTimer";
    private const string PluginVersion = "1.0.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;

    // Timer (host settings)
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<int> TimeLimitSeconds = null!;
    internal static ConfigEntry<bool> StopWhenAllExtracted = null!;

    // HUD (each player's own display)
    internal static ConfigEntry<bool> HudEnabled = null!;
    internal static ConfigEntry<int> HudPositionX = null!;
    internal static ConfigEntry<int> HudPositionY = null!;
    internal static ConfigEntry<int> HudFontSize = null!;
    internal static ConfigEntry<int> WarningSeconds = null!;

    private void Awake()
    {
        Instance = this;

        Enabled = Config.Bind(
            "Timer", "Enabled", true,
            "Kill every player when the level time limit runs out (host only; applies to all players)."
        );
        TimeLimitSeconds = Config.Bind(
            "Timer", "TimeLimitSeconds", 600,
            new ConfigDescription("Time limit per level in seconds", new AcceptableValueRange<int>(10, 7200))
        );
        StopWhenAllExtracted = Config.Bind(
            "Timer", "StopWhenAllExtracted", true,
            "Stop the timer once every extraction point is completed."
        );

        HudEnabled = Config.Bind(
            "HUD", "Enabled", true,
            "Show the remaining time on screen."
        );
        HudPositionX = Config.Bind(
            "HUD", "PositionX", 0,
            new ConfigDescription("HUD X offset from top center", new AcceptableValueRange<int>(-1000, 1000))
        );
        HudPositionY = Config.Bind(
            "HUD", "PositionY", -10,
            new ConfigDescription("HUD Y offset from top center", new AcceptableValueRange<int>(-1000, 1000))
        );
        HudFontSize = Config.Bind(
            "HUD", "FontSize", 32,
            new ConfigDescription("Font size", new AcceptableValueRange<int>(10, 80))
        );
        WarningSeconds = Config.Bind(
            "HUD", "WarningSeconds", 60,
            new ConfigDescription("Show the timer in red when this many seconds or fewer remain", new AcceptableValueRange<int>(0, 600))
        );

        new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded!");
    }
}
