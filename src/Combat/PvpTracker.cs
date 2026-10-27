using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;

namespace Aethermeter.Combat;

/// <summary>One knockout in a PvP match, on either side.</summary>
public sealed class KillRecord
{
    public double Time { get; init; }
    /// <summary>Our side scored it: an opponent went down. Otherwise one of ours did.</summary>
    public bool Ours { get; init; }

    public uint VictimId { get; init; }
    public string Victim { get; init; } = string.Empty;
    public uint VictimJob { get; init; }

    /// <summary>0 when the blow came from someone the meter doesn't follow.</summary>
    public uint KillerId { get; init; }
    public string Killer { get; init; } = string.Empty;
    public uint KillerJob { get; init; }

    /// <summary>Our side: everyone else who damaged the victim shortly before.</summary>
    public uint[] Assists { get; init; } = [];
}

/// <summary>
/// Works out who scored each PvP knockout. Every hit our side lands on an opposing player is
/// remembered; when that player goes down, the last hit (if recent) is the kill and everyone else
/// who hit them in the seconds before gets an assist. The game keeps its own scoreboard on the
/// server; this is our reading of the hits we saw, so it can differ by the odd assist.
/// </summary>
public sealed class PvpTracker
{
    /// <summary>The last hit counts as the kill only if it landed this close to the knockout.</summary>
    public const double KillingBlowWindow = 5.0;
    /// <summary>Damage this long before a knockout earns an assist.</summary>
    public const double AssistWindow = 15.0;

    private sealed class Rival
    {
        public required Actor Actor;
        public readonly Dictionary<uint, (double Time, Actor Who)> Hits = new();
        public uint LastHitter;
        public double LastHit = double.NegativeInfinity;
        public bool Down;
    }

    private readonly Dictionary<uint, Rival> rivals = new();

    public void Clear() => rivals.Clear();

    /// <summary>Called for every event of a PvP pull; only our hits on opponents matter.</summary>
    public void Note(in CombatEvent e)
    {
        if (e.Kind != EventKind.Damage || e.Amount <= 0 || !e.Source.Friendly || !e.Target.Hostile) return;

        if (!rivals.TryGetValue(e.Target.EntityId, out var r))
            rivals[e.Target.EntityId] = r = new Rival { Actor = e.Target };

        r.Actor = e.Target;
        r.Hits[e.Source.EntityId] = (e.Time, e.Source);
        r.LastHitter = e.Source.EntityId;
        r.LastHit = e.Time;
    }

    /// <summary>Per frame: opponents we hit who have just gone down.</summary>
    public List<KillRecord> Poll(double now)
    {
        var kills = new List<KillRecord>();
        foreach (var r in rivals.Values)
        {
            if (Plugin.ObjectTable.SearchByEntityId(r.Actor.EntityId) is not ICharacter ch) continue;

            var down = ch.IsDead || ch.CurrentHp == 0;
            if (!down)
            {
                r.Down = false;
                continue;
            }

            if (r.Down) continue;
            r.Down = true;
            if (KnockOut(r, now) is { } k) kills.Add(k);
        }

        return kills;
    }

    /// <summary>An opponent went down (the test fight calls this directly).</summary>
    public KillRecord? KnockOut(uint entityId, double now)
        => rivals.TryGetValue(entityId, out var r) ? KnockOut(r, now) : null;

    private static KillRecord? KnockOut(Rival r, double now)
    {
        var recent = r.Hits.Where(h => now - h.Value.Time <= AssistWindow).ToList();
        r.Hits.Clear();
        // Nobody of ours touched them lately: someone else's kill, not ours to count.
        if (recent.Count == 0) return null;

        var killer = now - r.LastHit <= KillingBlowWindow && recent.Any(h => h.Key == r.LastHitter)
            ? recent.First(h => h.Key == r.LastHitter).Value.Who
            : null;

        return new KillRecord
        {
            Time = now,
            Ours = true,
            VictimId = r.Actor.EntityId,
            Victim = r.Actor.Name,
            VictimJob = r.Actor.JobId,
            KillerId = killer?.EntityId ?? 0,
            Killer = killer?.Name ?? string.Empty,
            KillerJob = killer?.JobId ?? 0,
            Assists = recent.Where(h => h.Key != killer?.EntityId).Select(h => h.Key).ToArray(),
        };
    }

    /// <summary>One of ours went down: the death recap's last hit is the opponent who scored it.</summary>
    public KillRecord ForDeath(Combatant victim, DeathRecord death)
    {
        var blow = death.KillingBlow;
        var killer = blow is { } b ? rivals.Values.FirstOrDefault(r => r.Actor.Name == b.Source)?.Actor : null;
        return new KillRecord
        {
            Time = death.Time,
            Ours = false,
            VictimId = victim.EntityId,
            Victim = victim.Name,
            VictimJob = victim.JobId,
            KillerId = killer?.EntityId ?? 0,
            Killer = blow?.Source ?? string.Empty,
            KillerJob = killer?.JobId ?? 0,
        };
    }
}
