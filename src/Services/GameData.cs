using System;
using System.Collections.Generic;
using Dalamud.Game;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace Aethermeter.Services;

/// <summary>
/// Names, icons and job facts read from the game's sheets, so they follow the client language
/// and pick up new jobs and actions without an update. Results are memoised per id.
/// </summary>
public static class GameData
{
    public readonly record struct Entry(string Name, uint Icon);

    public enum Role : byte
    {
        None = 0,
        Tank = 1,
        Melee = 2,
        Ranged = 3,
        Healer = 4,
        Caster = 5,
    }

    public readonly record struct Job(string Name, string Abbreviation, Role Role, uint Icon);

    private static readonly Dictionary<uint, Entry> Actions = new();
    private static readonly Dictionary<uint, Entry> Statuses = new();
    private static readonly Dictionary<uint, Job> Jobs = new();
    private static readonly Dictionary<uint, StatusKind> Kinds = new();

    /// <summary>What a status does that the meter cares about.</summary>
    [System.Flags]
    public enum StatusKind : byte
    {
        None = 0,
        /// <summary>Deals damage every tick.</summary>
        Dot = 1,
        /// <summary>Restores HP every tick.</summary>
        Hot = 2,
        /// <summary>Soaks damage.</summary>
        Shield = 4,
    }

    /// <summary>The framed job icons start here, offset by ClassJob row.</summary>
    private const uint JobIconBase = 62100;

    /// <summary>The party list's chocobo icon: the same framed series as the jobs, slot 44.</summary>
    private const uint ChocoboIcon = JobIconBase + 44;

    public static Entry Action(uint id)
    {
        if (Actions.TryGetValue(id, out var cached)) return cached;
        var entry = new Entry(string.Empty, 0);
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Action>().TryGetRow(id, out var row))
                entry = new Entry(row.Name.ExtractText(), row.Icon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Action {Id} lookup failed", id);
        }

        return Actions[id] = entry;
    }

    public static Entry Status(uint id)
    {
        if (Statuses.TryGetValue(id, out var cached)) return cached;
        var entry = new Entry(string.Empty, 0);
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Status>().TryGetRow(id, out var row))
                entry = new Entry(row.Name.ExtractText(), row.Icon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Status {Id} lookup failed", id);
        }

        return Statuses[id] = entry;
    }

    public static Job JobInfo(uint id)
    {
        if (Jobs.TryGetValue(id, out var cached)) return cached;
        var job = new Job(string.Empty, string.Empty, Role.None, 0);
        try
        {
            if (id != 0 && Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(id, out var row))
            {
                var role = (Role)row.Role;
                // The sheet folds casters into "ranged"; their primary stat tells them apart.
                if (role == Role.Ranged && row.PrimaryStat == 4) role = Role.Caster;
                job = new Job(Capitalise(row.Name.ExtractText()), row.Abbreviation.ExtractText(), role, JobIconBase + id);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "ClassJob {Id} lookup failed", id);
        }

        return Jobs[id] = job;
    }

    /// <summary>What a combatant is shown as: its job, or for a chocobo, the companion itself.</summary>
    public static Job Profile(Combat.Combatant c)
    {
        if (!c.IsChocobo) return JobInfo(c.JobId);
        var word = ChocoboWord;
        return new Job(word, word, Role.None, ChocoboIcon);
    }

    private static string? chocoboWord;

    /// <summary>"Company Chocobo" in the client's language: the first mount is the companion's own bird.</summary>
    public static string ChocoboWord
    {
        get
        {
            if (chocoboWord is not null) return chocoboWord;
            chocoboWord = "Chocobo";
            try
            {
                if (Plugin.DataManager.GetExcelSheet<Mount>().TryGetRow(1, out var row) && row.Singular.ExtractText() is { Length: > 0 } name)
                    chocoboWord = Capitalise(name);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Mount lookup failed");
            }

            return chocoboWord;
        }
    }


    /// <summary>
    /// Whether a status ticks or shields. The sheet has no flag for either, but the English
    /// descriptions say so consistently ("Sustaining damage over time.", "Regenerating HP over time.",
    /// "A magicked barrier is nullifying damage."). The English sheet is read whatever the client
    /// language, so the answer is the same for everyone. Shields are also learned at runtime from
    /// the shield gauge, see <see cref="Combat.ShieldTracker"/>.
    /// </summary>
    public static StatusKind Kind(uint statusId)
    {
        if (statusId == 0) return StatusKind.None;
        if (Kinds.TryGetValue(statusId, out var cached)) return cached;

        var kind = StatusKind.None;
        try
        {
            if (Plugin.DataManager.GetExcelSheet<Status>(ClientLanguage.English).TryGetRow(statusId, out var row))
            {
                var text = row.Description.ExtractText();
                bool Has(string s) => text.Contains(s, System.StringComparison.OrdinalIgnoreCase);

                if (row.StatusCategory == 2 && Has("damage over time")) kind |= StatusKind.Dot;
                if (row.StatusCategory == 1 && (Has("HP over time") || Has("regenerating HP"))) kind |= StatusKind.Hot;
                if (row.StatusCategory == 1 && (Has("barrier") || Has("nullif") || Has("absorb"))) kind |= StatusKind.Shield;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Status {Id} kind lookup failed", statusId);
        }

        return Kinds[statusId] = kind;
    }

    /// <summary>A status the shield gauge proved to be a shield.</summary>
    public static void LearnShield(uint statusId) => Kinds[statusId] = Kind(statusId) | StatusKind.Shield;

    /// <summary>The name of whatever produced a hit: an action, the status behind a tick, or a shield.</summary>
    public static string AbilityName(uint actionId, uint statusId, bool shield = false)
    {
        if (actionId == 0 && statusId != 0)
        {
            var status = Status(statusId).Name;
            if (status.Length == 0) return shield ? "Shield" : "Over time";
            return status + (shield ? " (shield)" : " (tick)");
        }

        if (actionId == 0) return shield ? "Shield" : "Over time (unattributed)";
        var name = Action(actionId).Name;
        return name.Length > 0 ? name : $"Action #{actionId}";
    }

    public static uint AbilityIcon(uint actionId, uint statusId)
        => actionId == 0 && statusId != 0 ? Status(statusId).Icon : Action(actionId).Icon;

    /// <summary>Job names arrive in lower case in English ("white mage").</summary>
    private static string Capitalise(string text)
    {
        if (text.Length == 0) return text;
        var chars = text.ToCharArray();
        var start = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (start && char.IsLetter(chars[i])) chars[i] = char.ToUpperInvariant(chars[i]);
            start = chars[i] == ' ' || chars[i] == '-';
        }

        return new string(chars);
    }
}
