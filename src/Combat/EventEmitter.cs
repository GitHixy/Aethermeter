using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;

namespace Aethermeter.Combat;

/// <summary>
/// The last step before an event reaches the encounters: stamps the target's HP, estimates
/// overheal, and tells the shield tracker about damage. Hits, ticks and absorbs all pass here.
/// </summary>
public sealed class EventEmitter
{
    private readonly Action<CombatEvent> sink;

    /// <summary>HP we expect a target to have once effects already seen land, for overheal estimates.</summary>
    private readonly Dictionary<uint, (double Time, long Hp)> projected = new();

    public ShieldTracker? Shields { get; set; }

    public EventEmitter(Action<CombatEvent> sink) => this.sink = sink;

    public void Clear() => projected.Clear();

    public void Emit(CombatEvent e)
    {
        Stamp(ref e);
        Send(e);
    }

    /// <summary>
    /// One tick shared by several statuses. HP is read once for the whole tick, so overheal is
    /// shared out in proportion instead of landing on whichever part happened to go last.
    /// Parts sum to the tick exactly; only the split is an estimate.
    /// </summary>
    public void EmitSplit(CombatEvent tick, IReadOnlyList<(Actor Owner, Actor? Pet, uint StatusId, double Weight)> parts)
    {
        if (parts.Count == 0) return;
        Stamp(ref tick);

        var weights = 0.0;
        foreach (var p in parts) weights += Math.Max(p.Weight, 0);

        long given = 0, overGiven = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var (owner, pet, status, weight) = parts[i];
            var share = weights > 0 ? Math.Max(weight, 0) / weights : 1.0 / parts.Count;
            var last = i == parts.Count - 1;

            var e = tick;
            e.Source = owner;
            e.Pet = pet;
            e.StatusId = status;
            e.Amount = last ? tick.Amount - given : (long)Math.Round(tick.Amount * share);
            e.Overheal = last ? tick.Overheal - overGiven : (long)Math.Round(tick.Overheal * share);
            e.Overheal = Math.Clamp(e.Overheal, 0, Math.Max(e.Amount, 0));
            given += e.Amount;
            overGiven += e.Overheal;
            if (e.Amount > 0) Send(e);
        }
    }

    private void Send(in CombatEvent e)
    {
        if (e.Kind == EventKind.Damage && e.Target.Friendly && e.Amount > 0)
            Shields?.NoteDamage(e.Target.EntityId, e.Amount, e.Time);
        sink(e);
    }

    private void Stamp(ref CombatEvent e)
    {
        if (e.Target == Actor.Unknown || Plugin.ObjectTable.SearchByEntityId(e.Target.EntityId) is not ICharacter ch) return;

        e.TargetMaxHp = ch.MaxHp;
        long hp = ch.CurrentHp;

        // HP only moves once the server confirms the effect, a beat later; until then, count
        // what we already saw land so two quick heals don't both look effective.
        if (projected.TryGetValue(e.Target.EntityId, out var p) && e.Time - p.Time < 0.8)
            hp = p.Hp;

        e.TargetHp = (uint)Math.Clamp(hp, 0, uint.MaxValue);

        // An absorb already happened (the shield gauge dropped); it neither restores nor costs HP.
        if (e.Absorb) return;

        if (e.Kind == EventKind.Heal)
        {
            var missing = Math.Max(ch.MaxHp - hp, 0);
            e.Overheal = Math.Max(e.Amount - missing, 0);
            hp = Math.Min(hp + e.Amount, ch.MaxHp);
        }
        else if (e.Kind == EventKind.Damage)
            hp = Math.Max(hp - e.Amount, 0);

        projected[e.Target.EntityId] = (e.Time, hp);
    }
}
