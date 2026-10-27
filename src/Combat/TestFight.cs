using System;
using System.Collections.Generic;
using System.Linq;
using Aethermeter.Models;

namespace Aethermeter.Combat;

/// <summary>
/// /meter test: a made-up 40-second pull of a full party on a dummy, so the meter can be styled
/// and checked anywhere. /meter test pvp plays a minute of a five-on-five match instead, with
/// knockouts and respawns. Its events go through the same path as real ones.
/// </summary>
public sealed class TestFight
{
    private const double Length = 40.0;
    private const double MatchLength = 60.0;
    private const long PvpMaxHp = 52_000;
    private const double Respawn = 5.0;

    // Jobs by ClassJob row, with a rough damage weight and a few of their actions.
    private static readonly (uint Job, string Name, double Weight, uint[] Actions)[] Party =
    [
        (19, "Aria Lightbringer", 0.55, [9, 15, 21, 3539]),
        (32, "Noct Ashveil", 0.6, [3617, 3623, 3632, 7392]),
        (24, "Lys Dawnward", 0.35, [119, 7431, 16533]),
        (28, "Quill Starling", 0.33, [17869, 7435, 16539]),
        (22, "Kael Highwind", 1.0, [75, 78, 84, 3554, 7399]),
        (39, "Mora Vesper", 0.98, [24373, 24374, 24382, 24398]),
        (23, "Finch Arrowsong", 0.82, [97, 98, 7406, 3558]),
        (25, "Ilex Emberveil", 1.05, [141, 152, 3577, 7420]),
    ];

    private static readonly uint[] Heals = [124, 131, 135, 186, 189, 190, 3570];

    /// <summary>Our team in the test match: indexes into <see cref="Party"/>.</summary>
    private static readonly int[] Team = [0, 2, 4, 5, 7];

    private static readonly (uint Job, string Name)[] Opponents =
    [
        (21, "Brann Ironhold"),
        (28, "Sel Mistvale"),
        (30, "Vex Shadowstep"),
        (31, "Rook Gearwright"),
        (27, "Ondine Starfall"),
    ];

    private readonly EncounterManager encounters;
    private readonly CooldownTracker cooldowns;
    private readonly Random rng = new();
    private readonly Actor[] actors;
    private readonly Actor dummy = new() { EntityId = 0x40000001, Name = "Striking Dummy", Kind = ActorKind.Enemy };

    /// <summary>Kael's chocobo, fighting at his side.</summary>
    private readonly Actor chocobo = new()
    {
        EntityId = 0x40000002, Name = "Bocchan", Kind = ActorKind.Chocobo, Friendly = true,
        OwnerId = 0x10000005, OwnerName = "Kael Highwind",
    };
    private readonly Actor[] team;
    private readonly Actor[] rivals;
    /// <summary>Test match: everyone's HP, and when the knocked-out are back.</summary>
    private readonly Dictionary<uint, long> hp = new();
    private readonly Dictionary<uint, double> downUntil = new();
    private double started = -1;
    private double next;

    public bool Running => started >= 0;
    public bool Pvp { get; private set; }

    /// <summary>The made-up party, for the cooldown window while the test runs.</summary>
    public IReadOnlyList<Actor> Members => Pvp ? team : actors;

    public TestFight(EncounterManager encounters, CooldownTracker cooldowns)
    {
        this.encounters = encounters;
        this.cooldowns = cooldowns;
        actors = new Actor[Party.Length];
        for (var i = 0; i < Party.Length; i++)
            actors[i] = new Actor { EntityId = 0x10000001u + (uint)i, Name = Party[i].Name, JobId = Party[i].Job, Kind = ActorKind.Player, Friendly = true };

        team = Team.Select(i => actors[i]).ToArray();
        rivals = Opponents.Select((o, i) => new Actor
        {
            EntityId = 0x10000101u + (uint)i, Name = o.Name, JobId = o.Job, Kind = ActorKind.Player, Hostile = true,
        }).ToArray();
    }

    public void Start(double now, bool pvp = false)
    {
        cooldowns.ResetRecasts();
        started = now;
        next = now;
        Pvp = pvp;
        hp.Clear();
        downUntil.Clear();
        foreach (var a in team.Concat(rivals)) hp[a.EntityId] = PvpMaxHp;
        encounters.Simulating = true;
        encounters.SimulatingPvp = pvp;
    }

    public void Stop()
    {
        started = -1;
        encounters.Simulating = false;
        // A match only ends with the match: close it now, or it would wait for a zone change.
        if (encounters.SimulatingPvp) encounters.EndNow();
        encounters.SimulatingPvp = false;
    }

    public void Update(double now)
    {
        if (!Running) return;
        if (now - started > (Pvp ? MatchLength : Length))
        {
            Stop();
            return;
        }

        if (Pvp)
        {
            UpdateMatch(now);
            return;
        }

        // A burst of events every 0.25 s, roughly one GCD's worth per four ticks.
        while (next <= now)
        {
            next += 0.25;
            for (var i = 0; i < actors.Length; i++)
            {
                if (rng.NextDouble() > 0.45) continue;
                var (_, _, weight, actions) = Party[i];
                var crit = rng.NextDouble() < 0.25;
                var dh = rng.NextDouble() < 0.3;
                var amount = (long)(weight * rng.Next(9_000, 26_000) * (crit ? 1.6 : 1) * (dh ? 1.25 : 1));

                encounters.Ingest(new CombatEvent
                {
                    Time = now,
                    Kind = EventKind.Damage,
                    Source = actors[i],
                    Target = dummy,
                    ActionId = actions[rng.Next(actions.Length)],
                    Amount = amount,
                    Crit = crit,
                    DirectHit = dh,
                });
            }

            // Cooldowns: each one goes out a few seconds after it's ready, so buffs bunch up at the start.
            foreach (var who in actors)
                foreach (var def in CooldownCatalog.For(who.JobId, _ => true))
                    if (cooldowns.Remaining(who.EntityId, def, now) <= 0 && rng.NextDouble() < 0.05)
                        cooldowns.Record(who, def.ActionId, now);

            if (rng.NextDouble() < 0.3)
                encounters.Ingest(new CombatEvent
                {
                    Time = now, Kind = EventKind.Damage, Source = chocobo, Target = dummy,
                    ActionId = 7, Amount = rng.Next(3_000, 7_500), Crit = rng.NextDouble() < 0.1,
                });

            // Raid damage, then the healers answer it.
            if (rng.NextDouble() < 0.12)
            {
                foreach (var target in actors)
                    encounters.Ingest(new CombatEvent
                    {
                        Time = now, Kind = EventKind.Damage, Source = dummy, Target = target,
                        ActionId = 7, Amount = rng.Next(20_000, 48_000), TargetHp = 90_000, TargetMaxHp = 100_000,
                    });
            }

            if (rng.NextDouble() < 0.35)
            {
                var healer = actors[rng.Next(2, 4)];
                var target = actors[rng.Next(actors.Length)];
                var amount = rng.Next(8_000, 30_000);
                var over = rng.NextDouble() < 0.4 ? (long)(amount * rng.NextDouble()) : 0;
                encounters.Ingest(new CombatEvent
                {
                    Time = now, Kind = EventKind.Heal, Source = healer, Target = target,
                    ActionId = Heals[rng.Next(Heals.Length)], Amount = amount, Overheal = over, Crit = rng.NextDouble() < 0.2,
                });
            }
        }
    }

    // ------------------------------------------------------------------
    // Test match
    // ------------------------------------------------------------------

    private bool Up(Actor a, double now) => !downUntil.TryGetValue(a.EntityId, out var t) || now >= t;

    private void UpdateMatch(double now)
    {
        while (next <= now)
        {
            next += 0.25;

            foreach (var a in team.Concat(rivals))
                if (downUntil.TryGetValue(a.EntityId, out var t) && now >= t)
                {
                    downUntil.Remove(a.EntityId);
                    hp[a.EntityId] = PvpMaxHp;
                }

            // Fights bunch up: most hits go to one focus target on each side.
            for (var i = 0; i < team.Length; i++)
            {
                if (rng.NextDouble() > 0.3 || !Up(team[i], now)) continue;
                if (Pick(rivals, now, focus: 0) is not { } target) break;
                var actions = Party[Team[i]].Actions;
                Hit(team[i], target, actions[rng.Next(actions.Length)], rng.Next(4_000, 9_000), now);
            }

            for (var i = 0; i < rivals.Length; i++)
            {
                if (rng.NextDouble() > 0.27 || !Up(rivals[i], now)) continue;
                if (Pick(team, now, focus: 2) is not { } target) break;
                Hit(rivals[i], target, 7, rng.Next(4_000, 8_500), now);
            }

            // Our healer keeps whoever is lowest standing.
            var healer = team[1];
            if (!Up(healer, now) || rng.NextDouble() > 0.25) continue;
            var low = team.Where(a => Up(a, now)).MinBy(a => hp[a.EntityId]);
            if (low is null) continue;

            var amount = rng.Next(6_000, 12_000);
            var over = Math.Max(hp[low.EntityId] + amount - PvpMaxHp, 0);
            hp[low.EntityId] = Math.Min(hp[low.EntityId] + amount, PvpMaxHp);
            encounters.Ingest(new CombatEvent
            {
                Time = now, Kind = EventKind.Heal, Source = healer, Target = low,
                ActionId = Heals[rng.Next(Heals.Length)], Amount = amount, Overheal = over,
            });
        }
    }

    /// <summary>Usually the side's focus target while it stands (it moves on every few seconds), otherwise anyone still up.</summary>
    private Actor? Pick(Actor[] side, double now, int focus)
    {
        var up = side.Where(a => Up(a, now)).ToList();
        if (up.Count == 0) return null;
        var preferred = side[(focus + (int)((now - started) / 12)) % side.Length];
        return Up(preferred, now) && rng.NextDouble() < 0.7 ? preferred : up[rng.Next(up.Count)];
    }

    private void Hit(Actor source, Actor target, uint action, long amount, double now)
    {
        var crit = rng.NextDouble() < 0.15;
        if (crit) amount = (long)(amount * 1.5);
        var before = hp[target.EntityId];
        encounters.Ingest(new CombatEvent
        {
            Time = now, Kind = EventKind.Damage, Source = source, Target = target, ActionId = action,
            Amount = amount, Crit = crit, TargetHp = (uint)before, TargetMaxHp = (uint)PvpMaxHp,
        });

        hp[target.EntityId] = Math.Max(before - amount, 0);
        if (hp[target.EntityId] > 0) return;

        downUntil[target.EntityId] = now + Respawn;
        encounters.KnockOut(target, now);
    }
}
