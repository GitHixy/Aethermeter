using System;
using Aethermeter.Models;

namespace Aethermeter.Combat;

/// <summary>
/// /meter test: a made-up 40-second pull of a full party on a dummy, so the meter can be styled
/// and checked anywhere. Its events go through the same path as real ones.
/// </summary>
public sealed class TestFight
{
    private const double Length = 40.0;

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

    private readonly EncounterManager encounters;
    private readonly Random rng = new();
    private readonly Actor[] actors;
    private readonly Actor dummy = new() { EntityId = 0x40000001, Name = "Striking Dummy", Kind = ActorKind.Enemy };

    /// <summary>Kael's chocobo, fighting at his side.</summary>
    private readonly Actor chocobo = new()
    {
        EntityId = 0x40000002, Name = "Bocchan", Kind = ActorKind.Chocobo, Friendly = true,
        OwnerId = 0x10000005, OwnerName = "Kael Highwind",
    };
    private double started = -1;
    private double next;

    public bool Running => started >= 0;

    public TestFight(EncounterManager encounters)
    {
        this.encounters = encounters;
        actors = new Actor[Party.Length];
        for (var i = 0; i < Party.Length; i++)
            actors[i] = new Actor { EntityId = 0x10000001u + (uint)i, Name = Party[i].Name, JobId = Party[i].Job, Kind = ActorKind.Player, Friendly = true };
    }

    public void Start(double now)
    {
        started = now;
        next = now;
        encounters.Simulating = true;
    }

    public void Stop()
    {
        started = -1;
        encounters.Simulating = false;
    }

    public void Update(double now)
    {
        if (!Running) return;
        if (now - started > Length)
        {
            Stop();
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
}
