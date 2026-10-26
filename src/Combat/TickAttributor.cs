using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Aethermeter.Services;

namespace Aethermeter.Combat;

/// <summary>
/// Works out whose DoT or HoT a tick was.
///
/// The server leaves the status out of ordinary ticks (verified in game: status id 0), and
/// depending on the content it may send one tick per source or a single tick summing every DoT on
/// the target. ACT and FFLogs face the same data and estimate. We do it like this, per target and
/// per server tick (one frame):
///
/// 1. Ticks that do name a status (ground effects such as Salted Earth, Doton, Asylum) belong to
///    their source, or its owner when the source is the ground effect's own actor. Without a
///    source, the status on the target (or on a caster, for caster-side effects) names one.
/// 2. If every player with a DoT up on the target sent their own tick, each tick is theirs, exactly.
///    A player with two DoTs (Bard's bites) has the tick split between them.
/// 3. Otherwise the ticks are an aggregate: their sum is shared among every DoT on the target in
///    proportion to that DoT's learned per-tick value.
///
/// Per-tick values are learned from the exact cases (one DoT, own tick) and from aggregates where
/// only one DoT is unknown, so the split sharpens as the fight goes on. Totals always match what
/// the server sent; only the split of an aggregate is an estimate.
/// </summary>
public sealed class TickAttributor
{
    private readonly record struct Pending(uint TargetId, uint StatusId, bool Heal, long Value, uint SourceId, double Time);
    private readonly record struct Ticking(Actor Owner, Actor? Pet, uint StatusId);

    private readonly ActorResolver actors;
    private readonly EventEmitter emitter;
    private readonly List<Pending> pending = new();

    /// <summary>Learned per-tick value of a DoT/HoT, by owner and status.</summary>
    private readonly Dictionary<(uint Owner, uint Status), double> estimate = new();

    /// <summary>The DoT/HoT each caster last had on each target, to name a tick that outlived its status.</summary>
    private readonly Dictionary<(uint Target, uint Owner), uint> lastStatus = new();

    private int logged;

    public TickAttributor(ActorResolver actors, EventEmitter emitter)
    {
        this.actors = actors;
        this.emitter = emitter;
    }

    public void Clear()
    {
        pending.Clear();
        estimate.Clear();
        lastStatus.Clear();
    }

    /// <summary>Called from the tick hook: ticks of one server tick are settled together in <see cref="Flush"/>.</summary>
    /// <returns>Whether the tick heals, for the log line.</returns>
    public bool Queue(uint targetId, uint statusId, int tickMode, long value, uint sourceId, int damageType, double now)
    {
        var heal = IsHeal(targetId, statusId, tickMode);

        // The first few ticks of a session go to the log, so the raw values can be checked in /xllog.
        if (logged < 12)
        {
            logged++;
            Plugin.Log.Information("Tick: target={Target:X} status={Status} mode={Mode} value={Value} source={Source:X} type={Type} -> {Kind}",
                targetId, statusId, tickMode, value, sourceId, damageType, heal ? "heal" : "damage");
        }

        pending.Add(new Pending(targetId, statusId, heal, value, sourceId, now));
        return heal;
    }

    public void Flush()
    {
        if (pending.Count == 0) return;

        foreach (var group in pending.GroupBy(p => (p.TargetId, p.Heal)))
        {
            try
            {
                Settle(group.Key.TargetId, group.Key.Heal, group.ToList());
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not attribute a tick");
            }
        }

        pending.Clear();
    }

    private void Settle(uint targetId, bool heal, List<Pending> ticks)
    {
        var target = actors.Resolve(targetId);
        var obj = Plugin.ObjectTable.SearchByEntityId(targetId) as IBattleChara;
        var ticking = TickingOn(obj, heal);
        var time = ticks[0].Time;

        // 1. Ticks that name their status.
        var generic = new List<Pending>();
        foreach (var t in ticks)
        {
            if (t.StatusId == 0)
            {
                generic.Add(t);
                continue;
            }

            var source = ValidSource(t.SourceId) ? actors.Resolve(t.SourceId) : FindApplier(obj, t.StatusId);
            var owner = actors.Owner(source);
            Emit(heal, target, time, t.Value, owner, owner == source ? null : source, t.StatusId);
        }

        if (generic.Count == 0) return;

        foreach (var owner in ticking.GroupBy(k => k.Owner.EntityId).Where(g => g.Count() == 1))
            lastStatus[(targetId, owner.Key)] = owner.First().StatusId;

        // 2. One tick per caster: exact. Everyone counts here, not only us: players outside the
        // party with a DoT on the same boss send their own ticks too, and must not make ours look combined.
        var valid = generic.Where(t => ValidSource(t.SourceId)).ToList();
        var tickingOwners = ticking.Select(k => k.Owner.EntityId).Distinct().ToList();
        var coveredOwners = valid.Select(t => actors.Owner(actors.Resolve(t.SourceId)).EntityId).ToHashSet();
        var perSource = valid.Count == generic.Count && tickingOwners.All(coveredOwners.Contains);

        if (perSource)
        {
            foreach (var t in valid)
            {
                var source = actors.Resolve(t.SourceId);
                var owner = actors.Owner(source);
                var mine = ticking.Where(k => k.Owner.EntityId == owner.EntityId).ToList();

                if (mine.Count == 1)
                    Learn(owner.EntityId, mine[0].StatusId, t.Value);

                if (mine.Count > 1)
                {
                    Split(heal, target, time, t.Value, mine);
                    continue;
                }

                // The status may have just fallen off as its last tick landed: name it from before.
                var status = mine.Count == 1 ? mine[0].StatusId : lastStatus.GetValueOrDefault((targetId, owner.EntityId));
                Emit(heal, target, time, t.Value, owner, owner == source ? null : source, status);
            }

            return;
        }

        // 3. An aggregate: share the total by learned per-tick values.
        var total = generic.Sum(t => t.Value);
        if (ticking.Count == 0)
        {
            // Nothing on the target says whose it was. A lone tick with a valid source stays theirs;
            // otherwise it's only kept when it hurt one of us.
            var only = generic.Count == 1 && ValidSource(generic[0].SourceId) ? actors.Resolve(generic[0].SourceId) : null;
            if (only is not null)
            {
                var owner = actors.Owner(only);
                Emit(heal, target, time, total, owner, owner == only ? null : only, 0);
            }
            else if (target.Friendly)
                Emit(heal, target, time, total, Actor.Unknown, null, 0);
            return;
        }

        LearnFromAggregate(total, ticking);
        Split(heal, target, time, total, ticking);
    }

    private void Split(bool heal, Actor target, double time, long amount, List<Ticking> parts)
    {
        var known = parts.Select(k => estimate.GetValueOrDefault((k.Owner.EntityId, k.StatusId))).Where(v => v > 0).ToList();
        var fallback = known.Count > 0 ? known.Average() : 1.0;

        var weighted = parts
            .Select(k => (k.Owner, k.Pet, k.StatusId, Weight: estimate.TryGetValue((k.Owner.EntityId, k.StatusId), out var w) && w > 0 ? w : fallback))
            .ToList();

        emitter.EmitSplit(Template(heal, target, time, amount), weighted);
    }

    /// <summary>When every DoT in an aggregate but one is known, the remainder is that one's value.</summary>
    private void LearnFromAggregate(long total, List<Ticking> parts)
    {
        var unknown = parts.Where(k => !estimate.ContainsKey((k.Owner.EntityId, k.StatusId))).ToList();
        if (unknown.Count == 1 && parts.Count > 1)
        {
            var rest = total - parts.Where(k => k != unknown[0]).Sum(k => estimate[(k.Owner.EntityId, k.StatusId)]);
            if (rest > 0) Learn(unknown[0].Owner.EntityId, unknown[0].StatusId, rest);
        }
        else if (parts.Count == 1)
            Learn(parts[0].Owner.EntityId, parts[0].StatusId, total);
    }

    private void Learn(uint owner, uint status, double value)
    {
        var key = (owner, status);
        estimate[key] = estimate.TryGetValue(key, out var old) ? old * 0.75 + value * 0.25 : value;
    }

    private void Emit(bool heal, Actor target, double time, long amount, Actor owner, Actor? pet, uint status)
    {
        var e = Template(heal, target, time, amount);
        e.Source = owner;
        e.Pet = pet;
        e.StatusId = status;
        emitter.Emit(e);
    }

    private static CombatEvent Template(bool heal, Actor target, double time, long amount) => new()
    {
        Time = time,
        Kind = heal ? EventKind.Heal : EventKind.Damage,
        Source = Actor.Unknown,
        Target = target,
        Amount = amount,
    };

    /// <summary>Every DoT (or HoT) on the target, with the player it counts for.</summary>
    private List<Ticking> TickingOn(IBattleChara? obj, bool heal)
    {
        var list = new List<Ticking>();
        if (obj is null) return list;

        var want = heal ? GameData.StatusKind.Hot : GameData.StatusKind.Dot;
        foreach (var s in obj.StatusList)
        {
            if (s.StatusId == 0 || (GameData.Kind(s.StatusId) & want) == 0) continue;
            if (!ValidSource(s.SourceId)) continue;

            var source = actors.Resolve(s.SourceId);
            var owner = actors.Owner(source);
            if (owner == Actor.Unknown) continue;
            list.Add(new Ticking(owner, owner == source ? null : source, s.StatusId));
        }

        return list;
    }

    /// <summary>
    /// Who applied a status, for ticks that name one but no source: whoever put it on the target,
    /// or a party member carrying it themselves (caster-side ground effects).
    /// </summary>
    private Actor FindApplier(IBattleChara? target, uint statusId)
    {
        if (target is not null)
            foreach (var s in target.StatusList)
                if (s.StatusId == statusId && ValidSource(s.SourceId))
                    return actors.Resolve(s.SourceId);

        foreach (var id in actors.Party)
        {
            if (Plugin.ObjectTable.SearchByEntityId(id) is not IBattleChara member) continue;
            foreach (var s in member.StatusList)
                if (s.StatusId == statusId && s.SourceId == id)
                    return actors.Resolve(id);
        }

        return Actor.Unknown;
    }

    /// <summary>
    /// Damage or healing. A named status says which; otherwise the tick mode (3 DoT, 4 HoT), and if
    /// that's something else, what's ticking on the target.
    /// </summary>
    private bool IsHeal(uint targetId, uint statusId, int tickMode)
    {
        if (statusId != 0)
        {
            var kind = GameData.Kind(statusId);
            if ((kind & GameData.StatusKind.Hot) != 0) return true;
            if ((kind & GameData.StatusKind.Dot) != 0) return false;
            if (Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>().TryGetRow(statusId, out var row))
                return row.StatusCategory == 1;
        }

        if (tickMode == 4) return true;
        if (tickMode == 3) return false;

        var target = Plugin.ObjectTable.SearchByEntityId(targetId) as IBattleChara;
        return target is not null && actors.Resolve(targetId).Friendly && TickingOn(target, heal: true).Count > 0 && TickingOn(target, heal: false).Count == 0;
    }

    private static bool ValidSource(uint id) => id != 0 && id != ActorResolver.InvalidId;
}
