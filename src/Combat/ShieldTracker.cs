using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Aethermeter.Services;

namespace Aethermeter.Combat;

/// <summary>
/// Turns shields into healing, the way FFLogs counts them: damage a shield soaked is healing done
/// by whoever cast it.
///
/// The client keeps one number per character, the shield gauge (percent of max HP), plus the
/// shield statuses. So, per party member and per frame:
/// - the gauge going up while a shield status appears (or is refreshed) is that shield's size;
/// - the gauge going down right after damage is damage absorbed, taken from the oldest shield first;
/// - the gauge going down with no damage, or a shield status vanishing, is a shield expiring unused.
///
/// The gauge is a whole percent, so each reading is within half a percent of max HP. Absorbs are
/// differences between readings, so the error doesn't pile up over a fight: it stays under about
/// one percent of max HP per shield.
/// </summary>
public sealed class ShieldTracker
{
    /// <summary>Damage older than this can't explain a gauge drop.</summary>
    private const double DamageWindow = 2.0;

    private sealed class Shield
    {
        public uint StatusId;
        public uint SourceId;
        public float Remaining;
        public ushort Param;
        public long Value;
        public double Applied;
        /// <summary>Its timer or stacks went up this frame.</summary>
        public bool Refreshed;
    }

    private sealed class State
    {
        public long Gauge;
        public readonly List<Shield> Shields = new();
        public HashSet<(uint, uint)> Seen = new();
        public long DamagePool;
        public double DamageTime;
    }

    private readonly ActorResolver actors;
    private readonly EventEmitter emitter;
    private readonly Dictionary<uint, State> states = new();

    public ShieldTracker(ActorResolver actors, EventEmitter emitter)
    {
        this.actors = actors;
        this.emitter = emitter;
    }

    public void Clear() => states.Clear();

    public void NoteDamage(uint targetId, long amount, double now)
    {
        if (!states.TryGetValue(targetId, out var s)) return;
        if (now - s.DamageTime > DamageWindow) s.DamagePool = 0;
        s.DamagePool += amount;
        s.DamageTime = now;
    }

    /// <summary>Reads the gauge and shield statuses of everyone we follow.</summary>
    public void Update(IEnumerable<uint> ids, double now)
    {
        var alive = new HashSet<uint>();
        foreach (var id in ids)
        {
            if (Plugin.ObjectTable.SearchByEntityId(id) is not IBattleChara ch) continue;
            alive.Add(id);
            try
            {
                Track(id, ch, now);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Shield tracking failed");
            }
        }

        foreach (var gone in states.Keys.Where(k => !alive.Contains(k)).ToList())
            states.Remove(gone);
    }

    private void Track(uint id, IBattleChara ch, double now)
    {
        var gauge = (long)Math.Round(ch.ShieldPercentage * (double)ch.MaxHp / 100.0);

        // Every status, so a shield nobody described as one can still be learned from the gauge.
        var statuses = new List<(uint Status, uint Source, float Remaining, ushort Param)>();
        foreach (var st in ch.StatusList)
            if (st.StatusId != 0)
                statuses.Add((st.StatusId, st.SourceId, st.RemainingTime, st.Param));

        if (!states.TryGetValue(id, out var s))
        {
            // First sight: shields already up are split evenly over the gauge.
            s = new State { Gauge = gauge, Seen = statuses.Select(x => (x.Status, x.Source)).ToHashSet() };
            var initial = statuses.Where(x => IsShield(x.Status)).ToList();
            foreach (var x in initial)
                s.Shields.Add(new Shield { StatusId = x.Status, SourceId = x.Source, Remaining = x.Remaining, Param = x.Param, Value = gauge / initial.Count, Applied = now });
            states[id] = s;
            return;
        }

        if (now - s.DamageTime > DamageWindow) s.DamagePool = 0;

        var appeared = statuses.Where(x => !s.Seen.Contains((x.Status, x.Source))).ToList();
        var delta = gauge - s.Gauge;

        if (delta < 0)
            Absorb(id, s, -delta, now);

        // Statuses that ended take whatever they still held with them.
        s.Shields.RemoveAll(sh => !statuses.Any(x => x.Status == sh.StatusId && x.Source == sh.SourceId));

        foreach (var sh in s.Shields)
        {
            var cur = statuses.First(x => x.Status == sh.StatusId && x.Source == sh.SourceId);
            sh.Refreshed = cur.Remaining > sh.Remaining + 1f || cur.Param != sh.Param;
            sh.Remaining = cur.Remaining;
            sh.Param = cur.Param;
        }

        if (delta > 0)
            Grow(s, delta, appeared, now);
        else
            foreach (var x in appeared.Where(x => IsShield(x.Status)))
                s.Shields.Add(new Shield { StatusId = x.Status, SourceId = x.Source, Remaining = x.Remaining, Param = x.Param, Applied = now });

        s.Seen = statuses.Select(x => (x.Status, x.Source)).ToHashSet();
        s.Gauge = gauge;
    }

    /// <summary>The gauge rose: credit the shield that caused it.</summary>
    private static void Grow(State s, long delta, List<(uint Status, uint Source, float Remaining, ushort Param)> appeared, double now)
    {
        var fresh = appeared.Where(x => IsShield(x.Status)).ToList();

        // A single new status arriving with the gauge, from one of us: that's a shield, whatever its text says.
        if (fresh.Count == 0 && appeared.Count == 1 && IsBeneficial(appeared[0].Status))
        {
            GameData.LearnShield(appeared[0].Status);
            fresh = appeared;
        }

        if (fresh.Count > 0)
        {
            foreach (var x in fresh)
                s.Shields.Add(new Shield { StatusId = x.Status, SourceId = x.Source, Remaining = x.Remaining, Param = x.Param, Value = delta / fresh.Count, Applied = now });
            return;
        }

        // A shield already up was refreshed or re-armed (Haima's stacks): it gets the increase.
        var target = s.Shields.LastOrDefault(sh => sh.Refreshed) ?? s.Shields.LastOrDefault();
        if (target is null) return;
        target.Value += delta;
        target.Applied = now;
    }

    /// <summary>The gauge dropped: as much of it as recent damage explains was absorbed.</summary>
    private void Absorb(uint id, State s, long drop, double now)
    {
        var absorbed = Math.Min(drop, s.DamagePool);
        if (absorbed <= 0 || s.Shields.Count == 0)
        {
            Shrink(s, drop);
            return;
        }

        s.DamagePool -= absorbed;
        var target = actors.Resolve(id);
        var credits = new Dictionary<Shield, long>();

        // Oldest shield first; anything left beyond what we tracked goes to the newest one still up.
        var left = absorbed;
        foreach (var sh in s.Shields.OrderBy(sh => sh.Applied))
        {
            if (left <= 0) break;
            var take = Math.Min(left, sh.Value);
            if (take <= 0) continue;
            sh.Value -= take;
            credits[sh] = take;
            left -= take;
        }

        if (left > 0)
        {
            var last = s.Shields.OrderBy(sh => sh.Applied).Last();
            credits[last] = credits.GetValueOrDefault(last) + left;
        }

        foreach (var (sh, amount) in credits)
        {
            var source = actors.Resolve(sh.SourceId);
            var owner = actors.Owner(source);
            emitter.Emit(new CombatEvent
            {
                Time = now,
                Kind = EventKind.Heal,
                Absorb = true,
                Source = owner,
                Pet = owner == source ? null : source,
                Target = target,
                StatusId = sh.StatusId,
                Amount = amount,
            });
        }

        Shrink(s, drop - absorbed);
    }

    /// <summary>A drop no damage explains: shields wearing off. Taken from the oldest first.</summary>
    private static void Shrink(State s, long drop)
    {
        foreach (var sh in s.Shields.OrderBy(sh => sh.Applied))
        {
            if (drop <= 0) break;
            var take = Math.Min(drop, sh.Value);
            sh.Value -= take;
            drop -= take;
        }
    }

    private static bool IsShield(uint status) => (GameData.Kind(status) & GameData.StatusKind.Shield) != 0;

    private static bool IsBeneficial(uint status)
        => Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().TryGetRow(status, out var row) && row.StatusCategory == 1;
}
