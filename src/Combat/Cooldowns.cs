using System;
using System.Collections.Generic;
using System.Linq;
using Aethermeter.Services;

namespace Aethermeter.Combat;

/// <summary>What a tracked cooldown is for, which is also how the window groups and filters them.</summary>
public enum CooldownKind : byte
{
    RaidBuff,
    Mitigation,
    Healing,
    Invuln,
    Utility,
}

/// <summary>
/// One cooldown worth watching. <see cref="Recast"/> is the max-level value, traits included,
/// where it differs from the sheet's base. <see cref="Aliases"/> are other ids that start the same
/// recast: a lower-level version, or the pet action a command makes the fairy use.
/// </summary>
public sealed record CooldownDef(uint ActionId, CooldownKind Kind, float Recast, uint[] Jobs, params uint[] Aliases)
{
    public uint Icon => GameData.Action(ActionId).Icon;
    public string Name => GameData.Action(ActionId).Name;
}

/// <summary>The party cooldowns Aethermeter follows. Ids checked against the 7.x Action sheet.</summary>
public static class CooldownCatalog
{
    private static readonly uint[] Tanks = [19, 21, 32, 37];
    private static readonly uint[] Healers = [24, 28, 33, 40];
    private static readonly uint[] Melee = [20, 22, 30, 34, 39, 41];
    private static readonly uint[] Casters = [25, 27, 35, 42];

    public static readonly CooldownDef[] All =
    [
        // Raid buffs
        new(7396, CooldownKind.RaidBuff, 120, [20]),           // Brotherhood
        new(3557, CooldownKind.RaidBuff, 120, [22]),           // Battle Litany
        new(36957, CooldownKind.RaidBuff, 120, [30], 2248),    // Dokumori (Mug below 66)
        new(24405, CooldownKind.RaidBuff, 120, [39]),          // Arcane Circle
        new(118, CooldownKind.RaidBuff, 120, [23]),            // Battle Voice
        new(25785, CooldownKind.RaidBuff, 110, [23]),          // Radiant Finale
        new(15998, CooldownKind.RaidBuff, 120, [38]),          // Technical Step
        new(25801, CooldownKind.RaidBuff, 120, [27]),          // Searing Light
        new(7520, CooldownKind.RaidBuff, 120, [35]),           // Embolden
        new(34675, CooldownKind.RaidBuff, 120, [42]),          // Starry Muse
        new(16552, CooldownKind.RaidBuff, 120, [33]),          // Divination
        new(7436, CooldownKind.RaidBuff, 120, [28]),           // Chain Stratagem

        // Party mitigation
        new(7535, CooldownKind.Mitigation, 60, Tanks),         // Reprisal
        new(3540, CooldownKind.Mitigation, 90, [19]),          // Divine Veil
        new(7385, CooldownKind.Mitigation, 120, [19]),         // Passage of Arms
        new(7388, CooldownKind.Mitigation, 90, [21]),          // Shake It Off
        new(16471, CooldownKind.Mitigation, 90, [32]),         // Dark Missionary
        new(16160, CooldownKind.Mitigation, 90, [37]),         // Heart of Light
        new(7549, CooldownKind.Mitigation, 90, Melee),         // Feint
        new(7560, CooldownKind.Mitigation, 90, Casters),       // Addle
        new(7405, CooldownKind.Mitigation, 90, [23]),          // Troubadour (90 s from level 88)
        new(16889, CooldownKind.Mitigation, 90, [31]),         // Tactician (90 s from level 88)
        new(2887, CooldownKind.Mitigation, 120, [31]),         // Dismantle
        new(16012, CooldownKind.Mitigation, 90, [38]),         // Shield Samba (90 s from level 88)
        new(25857, CooldownKind.Mitigation, 120, [35]),        // Magick Barrier
        new(34685, CooldownKind.Mitigation, 120, [42]),        // Tempera Coat

        // Healer cooldowns
        new(16536, CooldownKind.Healing, 120, [24]),           // Temperance
        new(25862, CooldownKind.Healing, 180, [24]),           // Liturgy of the Bell
        new(7433, CooldownKind.Healing, 60, [24]),             // Plenary Indulgence
        new(3569, CooldownKind.Healing, 90, [24]),             // Asylum
        new(25868, CooldownKind.Healing, 120, [28]),           // Expedient
        new(188, CooldownKind.Healing, 30, [28]),              // Sacred Soil
        new(16538, CooldownKind.Healing, 120, [28], 805),      // Fey Illumination (and the fairy's own)
        new(16545, CooldownKind.Healing, 120, [28]),           // Summon Seraph
        new(37014, CooldownKind.Healing, 180, [28]),           // Seraphism
        new(16559, CooldownKind.Healing, 120, [33]),           // Neutral Sect
        new(3613, CooldownKind.Healing, 60, [33]),             // Collective Unconscious
        new(25874, CooldownKind.Healing, 180, [33]),           // Macrocosmos
        new(7439, CooldownKind.Healing, 60, [33]),             // Earthly Star
        new(24298, CooldownKind.Healing, 30, [40]),            // Kerachole
        new(24310, CooldownKind.Healing, 120, [40]),           // Holos
        new(24311, CooldownKind.Healing, 120, [40]),           // Panhaima
        new(24302, CooldownKind.Healing, 60, [40]),            // Physis II
        new(24318, CooldownKind.Healing, 120, [40]),           // Pneuma

        // Tank invulnerabilities
        new(30, CooldownKind.Invuln, 420, [19]),               // Hallowed Ground
        new(43, CooldownKind.Invuln, 240, [21]),               // Holmgang
        new(3638, CooldownKind.Invuln, 300, [32]),             // Living Dead
        new(16152, CooldownKind.Invuln, 360, [37]),            // Superbolide

        // Utility
        new(7561, CooldownKind.Utility, 40, [.. Healers, .. Casters]), // Swiftcast (40 s from level 94)
        new(7571, CooldownKind.Utility, 120, Healers),         // Rescue
    ];

    private static readonly Dictionary<uint, CooldownDef> ById = Build();

    private static Dictionary<uint, CooldownDef> Build()
    {
        var map = new Dictionary<uint, CooldownDef>();
        foreach (var def in All)
        {
            map[def.ActionId] = def;
            foreach (var alias in def.Aliases) map[alias] = def;
        }

        return map;
    }

    public static CooldownDef? Find(uint actionId) => ById.GetValueOrDefault(actionId);

    /// <summary>A job's cooldowns of the kinds asked for, in catalogue order.</summary>
    public static IEnumerable<CooldownDef> For(uint jobId, Func<CooldownKind, bool> include)
        => All.Where(d => include(d.Kind) && d.Jobs.Contains(jobId));

    public static string KindLabel(CooldownKind kind) => kind switch
    {
        CooldownKind.RaidBuff => "Raid buff",
        CooldownKind.Mitigation => "Party mitigation",
        CooldownKind.Healing => "Healing cooldown",
        CooldownKind.Invuln => "Invulnerability",
        _ => "Utility",
    };
}

public readonly record struct CooldownUse(Actor Who, CooldownDef Def, double Time);

/// <summary>
/// Who used which party cooldown, and when. Fed by the same ActionEffect hook as the meter: one
/// packet per cast, whoever it hit. Recasts are forgotten when a duty restarts or the zone changes,
/// as the game resets them too; the uses stay, so past pulls keep their timeline.
/// </summary>
public sealed class CooldownTracker : IDisposable
{
    private const int MaxUses = 4000;

    private readonly ActorResolver actors;
    private readonly object sync = new();
    private readonly Dictionary<(uint Entity, uint Action), double> last = new();
    private readonly List<CooldownUse> uses = new();

    public CooldownTracker(ActorResolver actors)
    {
        this.actors = actors;
        Plugin.DutyState.DutyRecommenced += OnDutyRecommenced;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyRecommenced -= OnDutyRecommenced;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

    private void OnDutyRecommenced(Dalamud.Game.DutyState.IDutyStateEventArgs args) => ResetRecasts();
    private void OnTerritoryChanged(uint territory) => ResetRecasts();

    /// <summary>From the hook: an action resolved. Cheap for the actions we don't follow.</summary>
    public void OnAction(uint casterId, uint actionId, double now)
    {
        if (CooldownCatalog.Find(actionId) is null) return;
        var who = actors.Owner(actors.Resolve(casterId));
        if (who.Kind != ActorKind.Player || !who.Friendly) return;
        Record(who, actionId, now);
    }

    public void Record(Actor who, uint actionId, double now)
    {
        if (CooldownCatalog.Find(actionId) is not { } def) return;
        lock (sync)
        {
            var key = (who.EntityId, def.ActionId);
            // A fairy order arrives twice (the command, then the fairy's action): count it once.
            if (last.TryGetValue(key, out var previous) && now - previous < 2.0) return;

            last[key] = now;
            uses.Add(new CooldownUse(who, def, now));
            if (uses.Count > MaxUses) uses.RemoveRange(0, uses.Count - MaxUses);
        }
    }

    /// <summary>Seconds until it's ready again; 0 when ready (or never seen used).</summary>
    public double Remaining(uint entityId, CooldownDef def, double now)
    {
        lock (sync)
            return last.TryGetValue((entityId, def.ActionId), out var t) ? Math.Max(t + def.Recast - now, 0) : 0;
    }

    /// <summary>When it last came off cooldown, or null if it never went on one.</summary>
    public double? ReadyAt(uint entityId, CooldownDef def)
    {
        lock (sync)
            return last.TryGetValue((entityId, def.ActionId), out var t) ? t + def.Recast : null;
    }

    /// <summary>Uses between two clock times, oldest first.</summary>
    public List<CooldownUse> Between(double from, double to)
    {
        lock (sync)
            return uses.Where(u => u.Time >= from && u.Time <= to).ToList();
    }

    public int CountSince(uint entityId, CooldownDef def, double from)
    {
        lock (sync)
            return uses.Count(u => u.Who.EntityId == entityId && ReferenceEquals(u.Def, def) && u.Time >= from);
    }

    public void ResetRecasts()
    {
        lock (sync) last.Clear();
    }

    public void Clear()
    {
        lock (sync)
        {
            last.Clear();
            uses.Clear();
        }
    }
}
