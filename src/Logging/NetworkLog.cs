using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Aethermeter.Combat;
using Aethermeter.Models;
using Aethermeter.Services;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Aethermeter.Logging;

public enum LiveLogState
{
    Off,
    /// <summary>Switched on, waiting for a duty to start writing.</summary>
    Waiting,
    Writing,
}

/// <summary>
/// Writes the fight as an ACT network log, the format the FFLogs parser (in the Archon App) reads,
/// so a log can be uploaded without ACT.
///
/// The lines that come straight from the server are taken where the game handles them: abilities
/// (21/22) from ActionEffect, DoT/HoT ticks (24) from ProcessHotDot, director updates (33: commence,
/// wipe, clear) from ProcessDirectorUpdate. The rest is the world as the client sees it, compared
/// frame to frame: combatants appearing and leaving (03/04), statuses (26/30), casts (20/23), deaths
/// (25), the zone, the player, the party and their stats (01/02/11/12).
///
/// The zone's lines are also kept in memory, so any pull can be saved as its own small file.
/// </summary>
public sealed unsafe class NetworkLog : IDisposable
{
    /// <summary>The ACT FFXIV plugin version whose line layout we write, declared in the 253 header line.</summary>
    public const string FormatVersion = "3.0.3.4";
    private const string FileVersion = "30304";
    private const int BufferCap = 1_000_000;
    private const uint NoTarget = ActorResolver.InvalidId;

    private readonly record struct Entry(double Clock, int Type, string Body);

    private sealed class Info
    {
        public uint Id;
        public string Name = string.Empty;
        public uint Job, Level, OwnerId, WorldId, NameId, BaseId;
        public string World = string.Empty;
        public uint Hp, MaxHp, Mp, MaxMp;
        public Vector3 Pos;
        public float Heading;
    }

    private sealed class Tracked
    {
        public Info Info = new();
        public Dictionary<(uint Status, uint Source), (float Remaining, ushort Param)> Statuses = new();
        public bool Dead;
        public bool Casting;
        public uint CastAction;
    }

    private readonly record struct PendingCancel(uint Caster, string Name, uint Action, double Clock, DateTimeOffset At);

    private readonly Configuration config;
    private readonly Func<double> clock;
    private readonly Func<bool> meterInCombat;

    private readonly List<Entry> buffer = new();
    private readonly Dictionary<uint, Tracked> tracked = new();
    private readonly Dictionary<uint, (uint Action, double Clock)> lastAction = new();
    private readonly Dictionary<uint, (uint Id, string Name)> lastAttacker = new();
    private readonly List<PendingCancel> cancels = new();
    private readonly List<(Encounter Pull, double At)> pendingSaves = new();

    private LogFile? live;
    private uint zone;
    private uint player;
    private string partyKey = string.Empty;
    private string statsKey = string.Empty;
    private bool? actCombat, gameCombat;
    private bool stale = true;
    private double lastCombat = double.NegativeInfinity;

    /// <summary>Whether lines are being produced at all (live, or kept for saving pulls).</summary>
    public bool Recording { get; private set; }

    public LiveLogState LiveState { get; private set; }
    public string? LivePath => live?.Path;
    public long LiveLines => live?.Lines ?? 0;

    public NetworkLog(Configuration config, Func<double> clock, Func<bool> meterInCombat)
    {
        this.config = config;
        this.clock = clock;
        this.meterInCombat = meterInCombat;
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aethermeter", "FFXIVLogs");

    /// <summary>Where ACT keeps its logs: the Archon App's default folder.</summary>
    public static string ActFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "FFXIVLogs");

    public string Folder => string.IsNullOrWhiteSpace(config.LogFolder) ? DefaultFolder : config.LogFolder;
    public string PullFolder => Path.Combine(Folder, "Pulls");

    public void Dispose() => CloseLive();

    // ------------------------------------------------------------------
    // Per frame
    // ------------------------------------------------------------------

    public void Update()
    {
        var now = clock();
        var local = Plugin.ObjectTable.LocalPlayer;
        if (!Plugin.ClientState.IsLoggedIn || local is null)
        {
            CloseLive();
            LiveState = config.LiveLog ? LiveLogState.Waiting : LiveLogState.Off;
            Recording = false;
            stale = true;
            return;
        }

        var territory = (uint)Plugin.ClientState.TerritoryType;
        var zoneChanged = territory != zone;
        if (zoneChanged)
        {
            zone = territory;
            buffer.Clear();
            stale = true;
        }

        // Pulls are kept from zone-in inside duties; in the open world, only around combat, so
        // walking through a city costs nothing.
        if (meterInCombat() || Plugin.Condition[ConditionFlag.InCombat]) lastCombat = now;
        var keepPulls = config.KeepPullLogs && (InDuty || now - lastCombat < 15);

        var liveWanted = config.LiveLog && (!config.LiveLogDutiesOnly || InDuty);
        Recording = liveWanted || keepPulls;
        LiveState = !config.LiveLog ? LiveLogState.Off : liveWanted ? LiveLogState.Writing : LiveLogState.Waiting;

        if (!Recording)
        {
            CloseLive();
            stale = true;
            return;
        }

        if (stale)
        {
            // Starting over (new zone, or recording resumed): everything is announced again.
            tracked.Clear();
            lastAttacker.Clear();
            cancels.Clear();
            player = 0;
            partyKey = statsKey = string.Empty;
            actCombat = gameCombat = null;
            stale = false;
            Emit(Line01());
        }

        // A new folder was picked: the live file moves there.
        if (live is not null && !string.Equals(Path.GetDirectoryName(live.Path), Path.GetFullPath(Folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            CloseLive();

        if (liveWanted && live is null) OpenLive();
        else if (!liveWanted && live is not null) CloseLive();

        if (local.EntityId != player)
        {
            player = local.EntityId;
            Emit(new LogLine(2, DateTimeOffset.Now).Id(player).Str(local.Name.TextValue));
        }

        var party = PartyIds();
        var key = string.Join(',', party);
        if (key != partyKey)
        {
            partyKey = key;
            Emit(Line11(party));
        }

        var stats = Line12(out var sKey);
        if (stats is not null && sKey != statsKey)
        {
            statsKey = sKey;
            Emit(stats);
        }

        var inAct = meterInCombat();
        var inGame = Plugin.Condition[ConditionFlag.InCombat];
        if (inAct != actCombat || inGame != gameCombat)
        {
            Emit(new LogLine(260, DateTimeOffset.Now).Dec(inAct ? 1 : 0).Dec(inGame ? 1 : 0)
                .Dec(inAct != actCombat ? 1 : 0).Dec(inGame != gameCombat ? 1 : 0));
            actCombat = inAct;
            gameCombat = inGame;
        }

        PollWorld(now);
        SettleCancels(now);
        SavePending(now);
        live?.FlushIfDue();
    }

    private static bool InDuty =>
        Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56] || Plugin.Condition[ConditionFlag.BoundByDuty95];

    private void Emit(LogLine line)
    {
        var body = line.Body;
        if (config.KeepPullLogs)
        {
            if (buffer.Count >= BufferCap) buffer.RemoveRange(0, BufferCap / 10);
            buffer.Add(new Entry(clock(), line.Type, body));
        }

        live?.Write(body);
    }

    // ------------------------------------------------------------------
    // Live file
    // ------------------------------------------------------------------

    private void OpenLive()
    {
        try
        {
            var path = Path.Combine(Folder, $"Network_{FileVersion}_{DateTime.Now:yyyyMMdd}_Aethermeter.log");
            live = new LogFile(path);

            // A file has to stand on its own: version, zone, player, party, stats, then everyone present.
            live.Write(Line253().Body);
            live.Write(Line01().Body);
            if (Plugin.ObjectTable.LocalPlayer is { } me)
                live.Write(new LogLine(2, DateTimeOffset.Now).Id(me.EntityId).Str(me.Name.TextValue).Body);
            live.Write(Line11(PartyIds()).Body);
            if (Line12(out _) is { } stats) live.Write(stats.Body);
            foreach (var t in tracked.Values)
            {
                live.Write(Combatant(3, t.Info).Body);
                foreach (var ((status, source), (remaining, param)) in t.Statuses)
                    live.Write(StatusLine(26, status, source, t, remaining, param).Body);
            }

            Plugin.Log.Information("Live log started: {Path}", path);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not open the live log");
            live = null;
            config.LiveLog = false;
        }
    }

    private void CloseLive()
    {
        if (live is null) return;
        try
        {
            live.Dispose();
            Plugin.Log.Information("Live log closed: {Path} ({Lines} lines)", live.Path, live.Lines);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not close the live log");
        }

        live = null;
    }

    // ------------------------------------------------------------------
    // Pull files
    // ------------------------------------------------------------------

    /// <summary>Whether this zone's memory still covers the pull.</summary>
    /// <remarks>Outside duties the memory starts with combat, a frame after the first hit, hence the second of slack.</remarks>
    public bool CanSave(Encounter pull)
        => !pull.IsOverall && config.KeepPullLogs && buffer.Count > 0 && buffer[0].Clock <= pull.Start + 1.0;

    public void QueueSave(Encounter pull)
    {
        if (config.AutoSavePullLogs && CanSave(pull))
            pendingSaves.Add((pull, clock() + 3.0));
    }

    private void SavePending(double now)
    {
        for (var i = pendingSaves.Count - 1; i >= 0; i--)
        {
            if (now < pendingSaves[i].At) continue;
            var pull = pendingSaves[i].Pull;
            pendingSaves.RemoveAt(i);
            try
            {
                SavePull(pull);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not save the pull log");
            }
        }
    }

    /// <summary>
    /// Writes one pull as a standalone log: the zone's setup lines, everyone who appeared before it
    /// ended, statuses from shortly before, and every line from the pull itself.
    /// </summary>
    public string SavePull(Encounter pull)
    {
        if (!CanSave(pull)) throw new InvalidOperationException("This pull is no longer in memory.");

        var start = pull.Start;
        var end = pull.End ?? clock();
        var safe = string.Concat(pull.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(PullFolder, $"Network_{FileVersion}_{pull.StartedAt:yyyyMMdd_HHmmss}_{safe}_Aethermeter.log");

        using var file = new LogFile(path);
        file.Write(Line253().Body);
        foreach (var e in buffer)
        {
            if (e.Clock > end + 3) break;
            var keep = e.Type switch
            {
                1 or 2 or 11 or 12 or 3 or 4 => true,
                26 or 30 => e.Clock >= start - 60,
                _ => e.Clock >= start - 2,
            };
            if (keep) file.Write(e.Body);
        }

        Plugin.Log.Information("Pull log saved: {Path}", path);
        return path;
    }

    // ------------------------------------------------------------------
    // Lines from the game hooks
    // ------------------------------------------------------------------

    public void OnActionEffect(uint casterId, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetIds)
    {
        if (!Recording || header == null) return;

        var now = DateTimeOffset.Now;
        lastAction[casterId] = (header->ActionId, clock());

        var caster = Plugin.ObjectTable.SearchByEntityId(casterId);
        var casterName = NameOf(casterId, caster);
        var ability = GameData.Action(header->ActionId).Name;
        var count = Math.Min((int)header->NumTargets, 32);
        var ownerId = caster is not null && caster.OwnerId is not 0 and not NoTarget ? caster.OwnerId : 0;

        void Tail(LogLine l, int index, int total)
        {
            Actor(l, caster);
            l.Str(header->GlobalSequence.ToString("X8")).Dec(index).Dec(total);
            if (ownerId != 0) l.Id(ownerId).Str(NameOf(ownerId, null));
            else l.Str("00").Str(string.Empty);
            l.Hex2(header->ActionType).Hex(header->ActionId).Hex(header->SpellId).F3(header->AnimationLock).Str(header->RotationInt.ToString("X4"));
        }

        if (count == 0)
        {
            // A cast that hit nobody still resolves; ACT logs it against no target.
            var l = new LogLine(21, now).Id(casterId).Str(casterName).Hex(header->ActionId).Str(ability).Id(NoTarget).Str(string.Empty);
            for (var j = 0; j < 16; j++) l.Str("0");
            l.Blank(10);
            Tail(l, 0, 1);
            Emit(l);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var targetId = targetIds[i].ObjectId;
            var target = Plugin.ObjectTable.SearchByEntityId(targetId);
            var l = new LogLine(count > 1 ? 22 : 21, now)
                .Id(casterId).Str(casterName).Hex(header->ActionId).Str(ability).Id(targetId).Str(NameOf(targetId, target));

            var list = effects[i].Effects;
            for (var j = 0; j < list.Length; j++)
            {
                ref readonly var fx = ref list[j];
                var flags = fx.Type | (uint)fx.Param0 << 8 | (uint)fx.Param1 << 16 | (uint)fx.Param2 << 24;
                var value = (uint)fx.Value << 16 | (uint)fx.Param4 << 8 | fx.Param3;
                l.Hex(flags).Hex(value);

                if (fx.Type is 3 or 5 or 6)
                    lastAttacker[(fx.Param4 & 0x80) != 0 ? casterId : targetId] = (casterId, casterName);
            }

            Actor(l, target);
            Tail(l, i, count);
            Emit(l);
        }
    }

    public void OnTick(uint targetId, uint statusId, bool heal, uint value, uint sourceId, int damageType)
    {
        if (!Recording) return;

        var target = Plugin.ObjectTable.SearchByEntityId(targetId);
        var source = sourceId is 0 or NoTarget ? null : Plugin.ObjectTable.SearchByEntityId(sourceId);
        var l = new LogLine(24, DateTimeOffset.Now)
            .Id(targetId).Str(NameOf(targetId, target)).Str(heal ? "HoT" : "DoT").Hex(statusId).Hex(value);
        Resources(l, target);
        l.Id(sourceId == 0 ? NoTarget : sourceId).Str(source is null ? string.Empty : NameOf(sourceId, source)).Hex((uint)damageType);
        Resources(l, source);
        Emit(l);

        if (!heal && source is not null)
            lastAttacker[targetId] = (sourceId, NameOf(sourceId, source));
    }

    public void OnDirector(uint instance, uint command, uint a1, uint a2, uint a3, uint a4)
    {
        if (!Recording) return;
        Emit(new LogLine(33, DateTimeOffset.Now).Id(instance).Id(command).Hex2(a1).Hex2(a2).Hex2(a3).Hex2(a4));
    }

    /// <summary>HP, MP and position as the 21/22 lines carry them, or blanks when the actor isn't loaded.</summary>
    private static void Actor(LogLine l, IGameObject? obj)
    {
        if (obj is ICharacter ch)
            l.Dec(ch.CurrentHp).Dec(ch.MaxHp).Dec(ch.CurrentMp).Dec(ch.MaxMp).Dec(0).Dec(1000).Pos(ch.Position, ch.Rotation);
        else
            l.Blank(10);
    }

    /// <summary>The same block as 24 lines carry it, with the two unknown fields left blank.</summary>
    private static void Resources(LogLine l, IGameObject? obj)
    {
        if (obj is ICharacter ch)
            l.Dec(ch.CurrentHp).Dec(ch.MaxHp).Dec(ch.CurrentMp).Dec(ch.MaxMp).Blank(2).Pos(ch.Position, ch.Rotation);
        else
            l.Blank(10);
    }

    // ------------------------------------------------------------------
    // The world, frame to frame
    // ------------------------------------------------------------------

    private readonly HashSet<uint> seen = new();

    private void PollWorld(double now)
    {
        seen.Clear();
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IBattleChara bc) continue;
            if (obj.ObjectKind is not (ObjectKind.Pc or ObjectKind.BattleNpc)) continue;
            var id = obj.EntityId;
            if (id is 0 or NoTarget || !seen.Add(id)) continue;

            if (!tracked.TryGetValue(id, out var t))
            {
                t = new Tracked();
                Fill(t.Info, bc);
                tracked[id] = t;
                Emit(Combatant(3, t.Info));
            }
            else
                Fill(t.Info, bc);

            Statuses(t, bc);
            Casts(t, bc, now);

            var dead = bc.IsDead || bc.CurrentHp == 0 && bc.MaxHp > 0;
            if (dead && !t.Dead)
            {
                var (killer, killerName) = lastAttacker.GetValueOrDefault(id, (NoTarget, string.Empty));
                Emit(new LogLine(25, DateTimeOffset.Now).Id(id).Str(t.Info.Name).Id(killer).Str(killerName));
            }

            t.Dead = dead;
        }

        foreach (var id in tracked.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Emit(Combatant(4, tracked[id].Info));
            tracked.Remove(id);
            lastAttacker.Remove(id);
        }
    }

    private static void Fill(Info info, IBattleChara bc)
    {
        info.Id = bc.EntityId;
        info.Name = bc.Name.TextValue;
        info.Job = bc.ClassJob.RowId;
        info.Level = bc.Level;
        info.OwnerId = bc.OwnerId is 0 or NoTarget ? 0 : bc.OwnerId;
        info.NameId = bc.ObjectKind == ObjectKind.BattleNpc ? bc.NameId : 0;
        info.BaseId = bc.ObjectKind == ObjectKind.BattleNpc ? bc.BaseId : 0;
        info.Hp = bc.CurrentHp;
        info.MaxHp = bc.MaxHp;
        info.Mp = bc.CurrentMp;
        info.MaxMp = bc.MaxMp;
        info.Pos = bc.Position;
        info.Heading = bc.Rotation;

        if (bc is IPlayerCharacter pc && info.WorldId == 0)
        {
            info.WorldId = pc.HomeWorld.RowId;
            info.World = pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }
    }

    private void Statuses(Tracked t, IBattleChara bc)
    {
        var now = new Dictionary<(uint, uint), (float, ushort)>();
        foreach (var s in bc.StatusList)
            if (s.StatusId != 0)
                now[(s.StatusId, s.SourceId)] = (s.RemainingTime, s.Param);

        foreach (var (key, (remaining, param)) in now)
        {
            // New, or refreshed (timer jumped up, or stacks changed): ACT logs both as a gain.
            if (!t.Statuses.TryGetValue(key, out var old) || remaining > old.Remaining + 0.5f || param != old.Param)
                Emit(StatusLine(26, key.Item1, key.Item2, t, remaining, param));
        }

        foreach (var (key, (_, param)) in t.Statuses)
            if (!now.ContainsKey(key))
                Emit(StatusLine(30, key.Status, key.Source, t, 0f, param));

        t.Statuses = now;
    }

    private LogLine StatusLine(int type, uint status, uint source, Tracked target, float duration, ushort param)
    {
        var src = tracked.GetValueOrDefault(source);
        return new LogLine(type, DateTimeOffset.Now)
            .Hex(status).Str(GameData.Status(status).Name).F2(duration)
            .Id(source == 0 ? NoTarget : source).Str(src?.Info.Name ?? (source is 0 or NoTarget ? string.Empty : NameOf(source, null)))
            .Id(target.Info.Id).Str(target.Info.Name).Hex2(param)
            .Dec(target.Info.MaxHp).Str(src is null ? string.Empty : src.Info.MaxHp.ToString());
    }

    private void Casts(Tracked t, IBattleChara bc, double now)
    {
        var casting = bc.IsCasting;
        var action = bc.CastActionId;

        if (casting && (!t.Casting || action != t.CastAction))
        {
            var targetId = (uint)bc.CastTargetObjectId;
            if (targetId == 0) targetId = NoTarget;
            Emit(new LogLine(20, DateTimeOffset.Now)
                .Id(t.Info.Id).Str(t.Info.Name).Hex(action).Str(GameData.Action(action).Name)
                .Id(targetId).Str(targetId == NoTarget ? string.Empty : NameOf(targetId, null))
                .F2(bc.TotalCastTime).Pos(bc.Position, bc.Rotation));
        }
        else if (!casting && t.Casting)
            cancels.Add(new PendingCancel(t.Info.Id, t.Info.Name, t.CastAction, now, DateTimeOffset.Now));

        t.Casting = casting;
        t.CastAction = casting ? action : 0;
    }

    /// <summary>A cast that ended without its action going off within a second was cancelled or interrupted.</summary>
    private void SettleCancels(double now)
    {
        for (var i = cancels.Count - 1; i >= 0; i--)
        {
            var c = cancels[i];
            if (lastAction.TryGetValue(c.Caster, out var a) && a.Action == c.Action && a.Clock >= c.Clock - 1.0)
            {
                cancels.RemoveAt(i);
                continue;
            }

            if (now - c.Clock < 1.0) continue;
            cancels.RemoveAt(i);
            Emit(new LogLine(23, c.At).Id(c.Caster).Str(c.Name).Hex(c.Action).Str(GameData.Action(c.Action).Name).Str("Cancelled"));
        }
    }

    // ------------------------------------------------------------------
    // Setup lines
    // ------------------------------------------------------------------

    private static LogLine Line253()
        => new LogLine(253, DateTimeOffset.Now).Str($"FFXIV PLUGIN VERSION: {FormatVersion}, CLIENT MODE: FFXIV");

    private LogLine Line01()
    {
        var name = string.Empty;
        if (Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().TryGetRow(zone, out var row))
            name = row.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
        return new LogLine(1, DateTimeOffset.Now).Hex(zone).Str(name);
    }

    private static List<uint> PartyIds()
    {
        var ids = new List<uint>();
        foreach (var m in Plugin.PartyList)
            if (m.EntityId is not 0 and not NoTarget) ids.Add(m.EntityId);
        if (ids.Count == 0 && Plugin.ObjectTable.LocalPlayer is { } me) ids.Add(me.EntityId);
        return ids;
    }

    private static LogLine Line11(List<uint> party)
    {
        var l = new LogLine(11, DateTimeOffset.Now).Dec(party.Count);
        foreach (var id in party) l.Id(id);
        return l;
    }

    /// <summary>The player's stats, read from the character sheet the game keeps.</summary>
    private static LogLine? Line12(out string key)
    {
        key = string.Empty;
        var ps = PlayerState.Instance();
        if (ps == null || ps->ContentId == 0) return null;

        var a = ps->Attributes.ToArray();
        int At(int baseParam) => baseParam < a.Length ? a[baseParam] : 0;

        // BaseParam rows: STR 1, DEX 2, VIT 3, INT 4, MND 5, PIE 6, Tenacity 19, Attack Power 20,
        // Direct Hit 22, Critical Hit 27, Attack/Healing Magic Potency 33/34, Determination 44,
        // Skill/Spell Speed 45/46.
        var l = new LogLine(12, DateTimeOffset.Now).Dec(ps->CurrentClassJobId)
            .Dec(At(1)).Dec(At(2)).Dec(At(3)).Dec(At(4)).Dec(At(5)).Dec(At(6))
            .Dec(At(20)).Dec(At(22)).Dec(At(27)).Dec(At(33)).Dec(At(34))
            .Dec(At(44)).Dec(At(45)).Dec(At(46)).Dec(0).Dec(At(19)).Hex(ps->ContentId);

        // Everything after the timestamp: a new line is written only when a stat actually changes.
        var body = l.Body;
        key = body[(body.IndexOf('|', body.IndexOf('|') + 1) + 1)..];
        return l;
    }

    private LogLine Combatant(int type, Info i)
    {
        var l = new LogLine(type, DateTimeOffset.Now)
            .Id(i.Id).Str(i.Name).Hex2(i.Job).Hex2(i.Level);
        if (i.OwnerId != 0) l.Id(i.OwnerId);
        else l.Str("0000");
        return l.Hex2(i.WorldId).Str(i.World).Dec(i.NameId).Dec(i.BaseId)
            .Dec(i.Hp).Dec(i.MaxHp).Dec(i.Mp).Dec(i.MaxMp).Dec(0).Dec(0).Pos(i.Pos, i.Heading);
    }

    private string NameOf(uint id, IGameObject? obj)
    {
        if (obj is not null) return obj.Name.TextValue;
        if (tracked.TryGetValue(id, out var t)) return t.Info.Name;
        return Plugin.ObjectTable.SearchByEntityId(id)?.Name.TextValue ?? string.Empty;
    }
}
