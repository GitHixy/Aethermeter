using System;
using System.Collections.Generic;

namespace Aethermeter.Combat;

/// <summary>Totals for one ability (or DoT/HoT) of one combatant.</summary>
public sealed class AbilityStats
{
    public uint ActionId { get; init; }
    public uint StatusId { get; init; }
    /// <summary>Set when a pet used it: shown as "Ability (Pet)".</summary>
    public string? PetName { get; init; }
    /// <summary>Damage taken only: who it came from.</summary>
    public string? SourceName { get; init; }
    /// <summary>A shield: <see cref="Total"/> is damage it absorbed.</summary>
    public bool IsShield { get; init; }

    public long Total;
    public long Overheal;
    public int Hits;
    public int Crits;
    public int DirectHits;
    public int Misses;
    public long Max;
    public long Min = long.MaxValue;

    public bool IsTick => ActionId == 0 && StatusId != 0 && !IsShield;

    public void Add(in CombatEvent e)
    {
        if (e.Kind == EventKind.Miss)
        {
            Misses++;
            return;
        }

        Total += e.Amount;
        Overheal += e.Overheal;
        Hits++;
        if (e.Crit) Crits++;
        if (e.DirectHit) DirectHits++;
        Max = Math.Max(Max, e.Amount);
        Min = Math.Min(Min, e.Amount);
    }
}

/// <summary>One line of a death recap: something that hit or healed the player shortly before.</summary>
public readonly record struct RecapLine(double Time, EventKind Kind, string Source, uint ActionId, uint StatusId, long Amount, uint HpBefore, uint MaxHp, bool Crit, bool Shield);

public sealed class DeathRecord
{
    public double Time { get; init; }
    public RecapLine[] Lines { get; init; } = [];

    /// <summary>The last thing that hit, which is what a death is usually blamed on.</summary>
    public RecapLine? KillingBlow
    {
        get
        {
            for (var i = Lines.Length - 1; i >= 0; i--)
                if (Lines[i].Kind == EventKind.Damage) return Lines[i];
            return null;
        }
    }
}

/// <summary>
/// One friendly actor within one segment. Pets are folded into their owner, keeping their
/// abilities apart in the breakdown.
/// </summary>
public sealed class Combatant
{
    private const int RecapSize = 14;

    public uint EntityId { get; }
    public string Name { get; set; }
    public uint JobId { get; set; }
    public bool IsLocalPlayer { get; set; }
    public bool IsAlly { get; set; }

    /// <summary>A chocobo companion, listed as its own line with its rider in brackets.</summary>
    public bool IsChocobo { get; }
    public uint OwnerId { get; }
    public string OwnerName { get; set; } = string.Empty;

    public long Damage;
    public long Healing;
    public long Overheal;
    /// <summary>Part of <see cref="Healing"/> that came from shields.</summary>
    public long Absorbed;
    public long DamageTaken;
    public long HealingTaken;
    public int Hits;
    public int Crits;
    public int DirectHits;
    public int Heals;
    public int HealCrits;
    public int Misses;

    public long MaxHit;
    public uint MaxHitAction;
    public uint MaxHitStatus;

    public Dictionary<(uint Action, uint Status, string? Pet, bool Shield), AbilityStats> DamageAbilities { get; } = new();
    public Dictionary<(uint Action, uint Status, string? Pet, bool Shield), AbilityStats> HealAbilities { get; } = new();
    public Dictionary<(uint Action, uint Status, string Source), AbilityStats> TakenAbilities { get; } = new();

    /// <summary>Damage and healing done per whole second since the segment began, for the graph.</summary>
    public List<float> DamageSeries { get; } = new();
    public List<float> HealSeries { get; } = new();

    public List<DeathRecord> Deaths { get; } = new();

    private readonly RecapLine[] recap = new RecapLine[RecapSize];
    private int recapCount;
    private int recapHead;

    public Combatant(Actor actor)
    {
        EntityId = actor.EntityId;
        Name = actor.Name;
        JobId = actor.JobId;
        IsLocalPlayer = actor.IsLocalPlayer;
        IsAlly = actor.Kind == ActorKind.Ally;
        IsChocobo = actor.Kind == ActorKind.Chocobo;
        OwnerId = actor.OwnerId;
        OwnerName = actor.OwnerName;
    }

    public void Refresh(Actor actor)
    {
        if (actor.Name.Length > 0) Name = actor.Name;
        if (actor.JobId != 0) JobId = actor.JobId;
        if (actor.OwnerName.Length > 0) OwnerName = actor.OwnerName;
        IsLocalPlayer |= actor.IsLocalPlayer;
    }

    public void AddDone(in CombatEvent e, int second)
    {
        var pet = e.Pet?.Name;
        if (e.Kind == EventKind.Heal)
        {
            Healing += e.Amount - e.Overheal;
            Overheal += e.Overheal;
            if (e.Absorb) Absorbed += e.Amount;
            Heals++;
            if (e.Crit) HealCrits++;
            Stat(HealAbilities, (e.ActionId, e.StatusId, pet, e.Absorb), e).Add(e);
            Bucket(HealSeries, second, e.Amount - e.Overheal);
            return;
        }

        Stat(DamageAbilities, (e.ActionId, e.StatusId, pet, false), e).Add(e);
        if (e.Kind == EventKind.Miss)
        {
            Misses++;
            return;
        }

        Damage += e.Amount;
        Hits++;
        if (e.Crit) Crits++;
        if (e.DirectHit) DirectHits++;
        if (e.Amount > MaxHit)
        {
            MaxHit = e.Amount;
            MaxHitAction = e.ActionId;
            MaxHitStatus = e.StatusId;
        }

        Bucket(DamageSeries, second, e.Amount);
    }

    public void AddTaken(in CombatEvent e)
    {
        if (e.Kind == EventKind.Heal)
            HealingTaken += e.Amount - e.Overheal;
        else if (e.Kind == EventKind.Damage)
        {
            DamageTaken += e.Amount;
            var key = (e.ActionId, e.StatusId, e.Source.Name);
            if (!TakenAbilities.TryGetValue(key, out var stats))
                TakenAbilities[key] = stats = new AbilityStats { ActionId = e.ActionId, StatusId = e.StatusId, SourceName = e.Source.Name };
            stats.Add(e);
        }
        else
            return;

        var source = e.Pet is not null ? e.Pet.Name : e.Source.Name;
        recap[recapHead] = new RecapLine(e.Time, e.Kind, source, e.ActionId, e.StatusId, e.Amount, e.TargetHp, e.TargetMaxHp, e.Crit, e.Absorb);
        recapHead = (recapHead + 1) % RecapSize;
        recapCount = Math.Min(recapCount + 1, RecapSize);
    }

    /// <summary>Freezes the last few things that happened to this player into a death record.</summary>
    public DeathRecord RecordDeath(double time, double window)
    {
        var lines = new List<RecapLine>(recapCount);
        for (var i = 0; i < recapCount; i++)
        {
            var line = recap[(recapHead - recapCount + i + RecapSize) % RecapSize];
            if (time - line.Time <= window) lines.Add(line);
        }

        recapCount = 0;
        var death = new DeathRecord { Time = time, Lines = lines.ToArray() };
        Deaths.Add(death);
        return death;
    }

    public float CritRate => Hits > 0 ? (float)Crits / Hits : 0f;
    public float DirectHitRate => Hits > 0 ? (float)DirectHits / Hits : 0f;
    public float OverhealRate => Healing + Overheal > 0 ? (float)Overheal / (Healing + Overheal) : 0f;

    private static AbilityStats Stat(Dictionary<(uint, uint, string?, bool), AbilityStats> map, (uint, uint, string?, bool) key, in CombatEvent e)
    {
        if (!map.TryGetValue(key, out var stats))
            map[key] = stats = new AbilityStats { ActionId = e.ActionId, StatusId = e.StatusId, PetName = e.Pet?.Name, IsShield = e.Absorb };
        return stats;
    }

    private static void Bucket(List<float> series, int second, long amount)
    {
        if (second < 0) return;
        while (series.Count <= second) series.Add(0f);
        series[second] += amount;
    }
}
