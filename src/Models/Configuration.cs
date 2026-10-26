using System;
using System.Collections.Generic;
using System.Numerics;

namespace Aethermeter.Models;

public enum TrackScope
{
    /// <summary>You, your party, their pets and Trust/Duty Support allies.</summary>
    Party,
    /// <summary>Every player around you, as in an alliance raid or a field operation.</summary>
    Everyone,
}

public enum NameMode
{
    Full,
    /// <summary>"Warrior Of-Light" becomes "W. O."</summary>
    Initials,
    /// <summary>The job name, numbered when two share it.</summary>
    Job,
    /// <summary>The job abbreviation, numbered when two share it.</summary>
    JobShort,
}

public enum BarStyle
{
    Flat,
    Gradient,
    Glass,
    Outline,
}

public enum BarColorMode
{
    Job,
    Role,
    Accent,
}

public enum NumberFormat
{
    /// <summary>12.3k, 1.45M</summary>
    Short,
    /// <summary>12,345</summary>
    Full,
}

public enum ReducedMotionMode
{
    System,
    On,
    Off,
}

/// <summary>How long Aethermeter keeps its own log files before the auto-clean removes them.</summary>
public enum LogRetention
{
    Never,
    OneWeek,
    TwoWeeks,
    OneMonth,
    ThreeMonths,
}

public enum FontId
{
    Interface,
    GameTrumpGothic,
    GameJupiter,
    BarlowCondensed,
    Barlow,
    BebasNeue,
}

/// <summary>One meter window: what it shows and where it lives.</summary>
[Serializable]
public sealed class MeterWindowConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Open { get; set; } = true;
    public MeterMode Mode { get; set; } = MeterMode.Dps;
    /// <summary>When set, the window keeps showing Overall instead of the current pull.</summary>
    public bool ShowOverall { get; set; }
    public bool Locked { get; set; }
    /// <summary>Locked windows can also let clicks through to the game.</summary>
    public bool ClickThrough { get; set; }
    public Vector2 Position { get; set; } = new(60, 320);
    public Vector2 Size { get; set; } = new(300, 220);
}

[Serializable]
public class Configuration
{
    public int Version { get; set; } = 1;
    public string LastSeenVersion { get; set; } = string.Empty;

    // ------------------------------------------------------------------
    // Tracking
    // ------------------------------------------------------------------

    public TrackScope TrackScope { get; set; } = TrackScope.Party;
    /// <summary>A pull also ends after this long without a hit, even if the game still says "in combat".</summary>
    public float IdleTimeoutSeconds { get; set; } = 20f;
    /// <summary>Pulls shorter than this with no damage are dropped.</summary>
    public float MinimumSegmentSeconds { get; set; } = 3f;
    public int SegmentsToKeep { get; set; } = 25;
    /// <summary>Writes every finished pull to the encounters folder.</summary>
    public bool AutoSaveEncounters { get; set; }

    // ------------------------------------------------------------------
    // Logs for FFLogs
    // ------------------------------------------------------------------

    /// <summary>The live log switch, also on the meter header.</summary>
    public bool LiveLog { get; set; }
    /// <summary>Live log only while in an instanced duty.</summary>
    public bool LiveLogDutiesOnly { get; set; } = true;
    /// <summary>Where logs go; empty means Documents\Aethermeter\FFXIVLogs.</summary>
    public string LogFolder { get; set; } = string.Empty;
    /// <summary>Keep this zone's log in memory so any pull can be saved as its own file.</summary>
    public bool KeepPullLogs { get; set; } = true;
    /// <summary>Write every finished pull as its own log file.</summary>
    public bool AutoSavePullLogs { get; set; }
    public LogRetention LogRetention { get; set; } = LogRetention.Never;
    /// <summary>The auto-clean also removes saved segment files.</summary>
    public bool CleanSavedSegments { get; set; }
    public DateTime LastLogCleanup { get; set; }

    // ------------------------------------------------------------------
    // Visibility
    // ------------------------------------------------------------------

    public bool HideOutOfCombat { get; set; }
    /// <summary>Seconds a hidden-out-of-combat meter lingers after a pull ends.</summary>
    public float LingerSeconds { get; set; } = 15f;
    public bool HideOutsideDuty { get; set; }
    public bool HideInCutscenes { get; set; } = true;
    public bool HideInGPose { get; set; } = true;
    public bool HideWithGameUi { get; set; } = true;

    // ------------------------------------------------------------------
    // Privacy
    // ------------------------------------------------------------------

    public NameMode NameMode { get; set; } = NameMode.Full;
    /// <summary>Show "You" in place of your own name, whatever the name mode.</summary>
    public bool ShowYou { get; set; }
    /// <summary>Keep your own name visible when the others are hidden.</summary>
    public bool KeepOwnName { get; set; }

    // ------------------------------------------------------------------
    // Look
    // ------------------------------------------------------------------

    public BarStyle BarStyle { get; set; } = BarStyle.Glass;
    public BarColorMode BarColorMode { get; set; } = BarColorMode.Job;
    public Vector4 AccentColor { get; set; } = new(0.37f, 0.85f, 0.78f, 1f);
    public float BarHeight { get; set; } = 22f;
    public float BarSpacing { get; set; } = 2f;
    public float BarOpacity { get; set; } = 0.85f;
    public float BackgroundOpacity { get; set; } = 0.72f;
    public float Rounding { get; set; } = 3f;
    public bool ShowJobIcons { get; set; } = true;
    public bool ShowRank { get; set; } = true;
    public bool ShowPercent { get; set; } = true;
    /// <summary>Show the total next to the per-second rate (or the rate next to the total).</summary>
    public bool ShowSecondary { get; set; } = true;
    public bool HighlightSelf { get; set; } = true;
    public bool ShowFooter { get; set; } = true;
    public bool TextShadow { get; set; } = true;
    public NumberFormat NumberFormat { get; set; } = NumberFormat.Short;
    public ReducedMotionMode ReducedMotion { get; set; } = ReducedMotionMode.System;

    public FontId NameFont { get; set; } = FontId.Interface;
    public FontId NumberFont { get; set; } = FontId.BarlowCondensed;
    public FontId HeaderFont { get; set; } = FontId.BarlowCondensed;
    public float NameFontSize { get; set; } = 15f;
    public float NumberFontSize { get; set; } = 17f;
    public float HeaderFontSize { get; set; } = 16f;

    public List<MeterWindowConfig> Windows { get; set; } = new();
}
