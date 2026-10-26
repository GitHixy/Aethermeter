using System;
using Aethermeter.Combat;

namespace Aethermeter.Models;

public enum MeterMode
{
    Dps,
    Damage,
    Hps,
    Healing,
    DamageTaken,
    Overhealing,
    Deaths,
}

public static class MeterModes
{
    public static readonly MeterMode[] All = Enum.GetValues<MeterMode>();

    public static string Label(MeterMode mode) => mode switch
    {
        MeterMode.Dps => "DPS",
        MeterMode.Damage => "Damage Done",
        MeterMode.Hps => "HPS",
        MeterMode.Healing => "Healing Done",
        MeterMode.DamageTaken => "Damage Taken",
        MeterMode.Overhealing => "Overhealing",
        MeterMode.Deaths => "Deaths",
        _ => mode.ToString(),
    };

    /// <summary>The value bars are sized and sorted by.</summary>
    public static double Value(MeterMode mode, Combatant c, double seconds) => mode switch
    {
        MeterMode.Dps => c.Damage / Math.Max(seconds, 1),
        MeterMode.Damage => c.Damage,
        MeterMode.Hps => c.Healing / Math.Max(seconds, 1),
        MeterMode.Healing => c.Healing,
        MeterMode.DamageTaken => c.DamageTaken,
        MeterMode.Overhealing => c.Overheal,
        MeterMode.Deaths => c.Deaths.Count,
        _ => 0,
    };

    /// <summary>The figure the percentage is a share of, and the companion number next to the main one.</summary>
    public static double Total(MeterMode mode, Combatant c) => mode switch
    {
        MeterMode.Dps or MeterMode.Damage => c.Damage,
        MeterMode.Hps or MeterMode.Healing => c.Healing,
        MeterMode.DamageTaken => c.DamageTaken,
        MeterMode.Overhealing => c.Overheal,
        MeterMode.Deaths => c.Deaths.Count,
        _ => 0,
    };

    public static bool IsRate(MeterMode mode) => mode is MeterMode.Dps or MeterMode.Hps;

    /// <summary>The rate that goes with a total (DPS for damage, HPS for healing), if any.</summary>
    public static MeterMode? Companion(MeterMode mode) => mode switch
    {
        MeterMode.Dps => MeterMode.Damage,
        MeterMode.Damage => MeterMode.Dps,
        MeterMode.Hps => MeterMode.Healing,
        MeterMode.Healing => MeterMode.Hps,
        MeterMode.DamageTaken => null,
        _ => null,
    };

    public static bool IsHealing(MeterMode mode) => mode is MeterMode.Hps or MeterMode.Healing or MeterMode.Overhealing;
}
