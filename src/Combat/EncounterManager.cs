using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.DutyState;
using Aethermeter.Models;

namespace Aethermeter.Combat;

/// <summary>Which segment a meter window shows.</summary>
public readonly record struct SegmentRef(SegmentKind Kind, int Id = 0)
{
    public static readonly SegmentRef Current = new(SegmentKind.Current);
    public static readonly SegmentRef Overall = new(SegmentKind.Overall);
}

public enum SegmentKind
{
    /// <summary>The running pull, or the last one once it ends.</summary>
    Current,
    Overall,
    /// <summary>A specific past pull, by id.</summary>
    Fixed,
}

/// <summary>
/// Cuts the event stream into pulls. A pull starts with the first hit between us and anything
/// else, and ends once nobody we follow has been in combat for a moment (or on a wipe, a clear,
/// or a zone change). Every pull also feeds the Overall segment.
/// </summary>
public sealed class EncounterManager : IDisposable
{
    /// <summary>Death recaps reach back this far.</summary>
    private const double RecapWindow = 12.0;
    /// <summary>Out-of-combat grace before a pull ends, so a short phase transition doesn't split it.</summary>
    private const double CombatGrace = 2.5;

    private readonly Configuration config;
    private readonly ActorResolver actors;
    private readonly Func<double> clock;

    /// <summary>Guards every segment: events arrive from game hooks, drawing reads them.</summary>
    public object Sync { get; } = new();

    public Encounter? Active { get; private set; }
    public Encounter Overall { get; private set; }

    /// <summary>Finished pulls, newest first.</summary>
    public List<Encounter> History { get; } = new();

    public event Action<Encounter>? Ended;

    private int nextId = 1;
    private double lastEvent;
    private double outOfCombatSince = -1;
    private readonly HashSet<uint> dead = new();

    public EncounterManager(Configuration config, ActorResolver actors, Func<double> clock)
    {
        this.config = config;
        this.actors = actors;
        this.clock = clock;
        Overall = new Encounter(0, clock(), isOverall: true);

        Plugin.DutyState.DutyWiped += OnDutyReset;
        Plugin.DutyState.DutyCompleted += OnDutyReset;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyWiped -= OnDutyReset;
        Plugin.DutyState.DutyCompleted -= OnDutyReset;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

    public bool InCombat => Active is not null;

    /// <summary>A test fight is running: its made-up party counts as in combat.</summary>
    public bool Simulating { get; set; }

    /// <summary>The party plus NPC allies seen in the current pull: everyone whose shields count.</summary>
    public List<uint> TrackedIds()
    {
        var ids = new List<uint>(actors.Party);
        lock (Sync)
        {
            if (Active is not null)
                foreach (var c in Active.Combatants.Values)
                    if ((c.IsAlly || c.IsChocobo) && !ids.Contains(c.EntityId)) ids.Add(c.EntityId);
        }

        return ids;
    }

    public Encounter? Resolve(SegmentRef segment) => segment.Kind switch
    {
        SegmentKind.Overall => Overall,
        SegmentKind.Fixed => History.FirstOrDefault(e => e.Id == segment.Id),
        _ => Active ?? History.FirstOrDefault(),
    };

    public void Ingest(CombatEvent e)
    {
        lock (Sync)
        {
            if (Active is null)
            {
                // Only a hit between sides starts a pull; heals and misses alone never do.
                var opensCombat = e.Kind == EventKind.Damage && e.Source.Friendly != e.Target.Friendly;
                if (!opensCombat) return;
                Begin(e.Time);
            }

            Active!.Apply(e);
            Overall.Apply(e);
            if (e.Kind == EventKind.Damage && e.Source.Friendly != e.Target.Friendly)
                lastEvent = e.Time;
        }
    }

    /// <summary>Per frame: ends pulls and catches deaths.</summary>
    public void Update()
    {
        if (Active is null) return;
        var now = clock();

        lock (Sync)
        {
            CheckDeaths(now);

            if (AnyoneInCombat())
                outOfCombatSince = -1;
            else if (outOfCombatSince < 0)
                outOfCombatSince = now;

            var idle = now - lastEvent > config.IdleTimeoutSeconds;
            if (idle || outOfCombatSince >= 0 && now - outOfCombatSince > CombatGrace)
                Finish(Math.Min(now, lastEvent + 1.0));
        }
    }

    public void Reset()
    {
        lock (Sync)
        {
            Active = null;
            History.Clear();
            Overall = new Encounter(0, clock(), isOverall: true);
            dead.Clear();
            actors.Clear();
        }
    }

    private void Begin(double now)
    {
        Active = new Encounter(nextId++, now) { Zone = ZoneName() };
        Overall.BeginLive(now);
        lastEvent = now;
        outOfCombatSince = -1;
        dead.Clear();
    }

    private void Finish(double end)
    {
        var pull = Active;
        if (pull is null) return;

        pull.End = Math.Max(end, pull.Start);
        Overall.EndLive(pull.End.Value);
        Active = null;

        if (pull.Duration(end) < config.MinimumSegmentSeconds && pull.TotalDamage == 0)
            return;

        History.Insert(0, pull);
        while (History.Count > Math.Max(config.SegmentsToKeep, 1))
            History.RemoveAt(History.Count - 1);

        Ended?.Invoke(pull);
    }

    private void OnDutyReset(IDutyStateEventArgs args) => EndNow();
    private void OnTerritoryChanged(uint territory) => EndNow();

    private void EndNow()
    {
        lock (Sync)
        {
            if (Active is not null) Finish(lastEvent + 0.5);
        }
    }

    private bool AnyoneInCombat()
    {
        if (Simulating || Plugin.Condition[ConditionFlag.InCombat]) return true;
        foreach (var id in actors.Party)
            if (Plugin.ObjectTable.SearchByEntityId(id) is ICharacter ch && (ch.StatusFlags & StatusFlags.InCombat) != 0)
                return true;
        return false;
    }

    private void CheckDeaths(double now)
    {
        foreach (var c in Active!.Combatants.Values)
        {
            var obj = Plugin.ObjectTable.SearchByEntityId(c.EntityId) as ICharacter;
            if (obj is null) continue;

            var isDead = obj.IsDead || obj.CurrentHp == 0;
            if (!isDead)
            {
                dead.Remove(c.EntityId);
                continue;
            }

            if (!dead.Add(c.EntityId)) continue;

            c.RecordDeath(now, RecapWindow);
            Active.TotalDeaths++;
            if (Overall.Combatants.TryGetValue(c.EntityId, out var overall))
            {
                overall.RecordDeath(now, RecapWindow);
                Overall.TotalDeaths++;
            }
        }
    }

    private static string ZoneName()
    {
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().TryGetRow(Plugin.ClientState.TerritoryType, out var row))
                return row.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Zone name lookup failed");
        }

        return string.Empty;
    }
}
