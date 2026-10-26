using System;
using System.Collections.Generic;
using System.Linq;

namespace Aethermeter.Combat;

/// <summary>
/// A segment: one pull, or the Overall segment that adds every pull up.
/// Only touched under <see cref="EncounterManager.Sync"/>.
/// </summary>
public sealed class Encounter
{
    public int Id { get; }
    public bool IsOverall { get; }
    public DateTime StartedAt { get; private set; }
    public string Zone { get; set; } = string.Empty;

    /// <summary>Clock time the segment began, and ended once it has.</summary>
    public double Start { get; private set; }
    public double? End { get; set; }

    /// <summary>Overall only: combat time banked from pulls that already ended.</summary>
    public double BankedSeconds { get; set; }

    public bool Active => End is null;

    public Dictionary<uint, Combatant> Combatants { get; } = new();
    public Dictionary<string, long> EnemyDamage { get; } = new();

    public long TotalDamage;
    public long TotalHealing;
    public long TotalTaken;
    public int TotalDeaths;

    /// <summary>For the Overall segment: where the running pull started on its timeline.</summary>
    private double liveOffset;
    private double liveStart = -1;

    public Encounter(int id, double start, bool isOverall = false)
    {
        Id = id;
        Start = start;
        StartedAt = DateTime.Now;
        IsOverall = isOverall;
        if (isOverall) End = start;
    }

    /// <summary>The enemy that took the most damage stands in for the fight's name.</summary>
    public string Title
    {
        get
        {
            if (IsOverall) return "Overall";
            var top = EnemyDamage.Count > 0 ? EnemyDamage.MaxBy(p => p.Value).Key : null;
            return top ?? (Zone.Length > 0 ? Zone : "Combat");
        }
    }

    public double Duration(double now)
    {
        if (IsOverall)
            return BankedSeconds + (liveStart >= 0 ? now - liveStart : 0);
        return Math.Max((End ?? now) - Start, 0);
    }

    /// <summary>Overall: a pull started; its seconds now run on top of the banked ones.</summary>
    public void BeginLive(double now)
    {
        if (Combatants.Count == 0 && BankedSeconds == 0)
        {
            Start = now;
            StartedAt = DateTime.Now;
        }

        liveOffset = BankedSeconds;
        liveStart = now;
        End = null;
    }

    public void EndLive(double now)
    {
        if (liveStart < 0) return;
        BankedSeconds += now - liveStart;
        liveStart = -1;
        End = now;
    }

    private int Second(double time)
        => IsOverall ? (int)(liveOffset + (liveStart >= 0 ? time - liveStart : 0)) : (int)(time - Start);

    public void Apply(in CombatEvent e)
    {
        var second = Second(e.Time);

        if (e.Source.Friendly && !e.Target.Friendly || e.Kind == EventKind.Heal && e.Source.Friendly)
        {
            var c = Get(e.Source);
            c.AddDone(e, second);
            if (e.Kind == EventKind.Damage)
            {
                TotalDamage += e.Amount;
                if (e.Target.Kind == ActorKind.Enemy && e.Target.Name.Length > 0)
                    EnemyDamage[e.Target.Name] = EnemyDamage.GetValueOrDefault(e.Target.Name) + e.Amount;
            }
            else if (e.Kind == EventKind.Heal)
                TotalHealing += e.Amount - e.Overheal;
        }

        if (e.Target.Friendly && e.Target.Kind != ActorKind.Pet)
        {
            // Self-inflicted damage (a few mechanics and job costs) is not damage taken from the fight.
            if (e.Kind == EventKind.Damage && e.Source.Friendly) return;

            Get(e.Target).AddTaken(e);
            if (e.Kind == EventKind.Damage) TotalTaken += e.Amount;
        }
    }

    public Combatant Get(Actor actor)
    {
        if (!Combatants.TryGetValue(actor.EntityId, out var c))
            Combatants[actor.EntityId] = c = new Combatant(actor);
        else
            c.Refresh(actor);
        return c;
    }
}
