using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aethermeter.Combat;
using Aethermeter.Models;

namespace Aethermeter.Services;

/// <summary>
/// Streamer mode: every player name the meter shows goes through here. Job-based names are
/// numbered by order of appearance within the segment, so they stay stable while you watch.
/// </summary>
public sealed class Names
{
    private readonly Configuration config;
    private readonly Dictionary<uint, string> segmentNames = new();
    private Encounter? segment;
    private NameMode mode;

    public Names(Configuration config) => this.config = config;

    public bool Hiding => config.NameMode != NameMode.Full;

    /// <summary>The name to show for a combatant of <paramref name="encounter"/>.</summary>
    public string For(Combatant c, Encounter encounter)
    {
        if (c.IsChocobo) return Chocobo(c, encounter);
        if (c.IsLocalPlayer && config.ShowYou) return "You";
        if (config.NameMode == NameMode.Full || c.IsAlly || c.IsLocalPlayer && config.KeepOwnName) return c.Name;

        if (!ReferenceEquals(segment, encounter) || mode != config.NameMode || !segmentNames.ContainsKey(c.EntityId))
            Rebuild(encounter);

        return segmentNames.TryGetValue(c.EntityId, out var name) ? name : Initials(c.Name);
    }

    /// <summary>A name seen outside a segment (a death recap source, a tooltip): hidden if it's a player's.</summary>
    public string Mask(string name, Encounter encounter)
    {
        if (config.NameMode == NameMode.Full) return name;
        foreach (var c in encounter.Combatants.Values)
            if (c.Name == name && !c.IsChocobo) return For(c, encounter);
        foreach (var c in encounter.Combatants.Values)
            if (c.Name == name) return For(c, encounter);
        return name;
    }

    /// <summary>
    /// A party member outside any segment (the cooldown window). Same rules as <see cref="For(Combatant, Encounter)"/>,
    /// with jobs numbered within <paramref name="roster"/>.
    /// </summary>
    public string For(Actor a, IReadOnlyList<Actor> roster)
    {
        if (a.IsLocalPlayer && config.ShowYou) return "You";
        if (config.NameMode == NameMode.Full || a.Kind == ActorKind.Ally || a.IsLocalPlayer && config.KeepOwnName) return a.Name;
        if (config.NameMode == NameMode.Initials) return Initials(a.Name);

        var job = GameData.JobInfo(a.JobId);
        var label = config.NameMode == NameMode.JobShort ? job.Abbreviation : job.Name;
        if (label.Length == 0) label = "Player";
        var same = roster.Where(r => r.JobId == a.JobId).OrderBy(r => r.EntityId).ToList();
        return same.Count <= 1 ? label : $"{label} {same.FindIndex(r => r.EntityId == a.EntityId) + 1}";
    }

    /// <summary>A PvP opponent, in the kill feed: hidden the same way as our own side.</summary>
    public string Opponent(string name, uint jobId)
    {
        switch (config.NameMode)
        {
            case NameMode.Full:
                return name;
            case NameMode.Initials:
                return Initials(name);
        }

        var job = GameData.JobInfo(jobId);
        var label = config.NameMode == NameMode.JobShort ? job.Abbreviation : job.Name;
        return label.Length > 0 ? label : "Opponent";
    }

    public void Invalidate() => segmentNames.Clear();

    /// <summary>
    /// "Name (Rider)". The rider goes through the same rules as everyone else; with names hidden,
    /// the chocobo's own name (which players pick, so it can identify them) becomes the plain word.
    /// </summary>
    private string Chocobo(Combatant c, Encounter encounter)
    {
        var own = config.NameMode == NameMode.Full || c.Name.Length == 0 ? c.Name : GameData.ChocoboWord;
        if (own.Length == 0) own = GameData.ChocoboWord;

        string rider;
        if (encounter.Combatants.TryGetValue(c.OwnerId, out var owner) && !owner.IsChocobo)
            rider = For(owner, encounter);
        else if (c.OwnerName.Length == 0)
            return own;
        else
            rider = config.NameMode == NameMode.Full ? c.OwnerName : Initials(c.OwnerName);

        // Your own bird keeps its name whenever yours would be shown.
        if (owner is { IsLocalPlayer: true } && (config.KeepOwnName || config.NameMode == NameMode.Full))
            own = c.Name.Length > 0 ? c.Name : own;

        return $"{own} ({rider})";
    }

    private void Rebuild(Encounter encounter)
    {
        segment = encounter;
        mode = config.NameMode;
        segmentNames.Clear();

        if (mode == NameMode.Initials)
        {
            foreach (var c in encounter.Combatants.Values)
                segmentNames[c.EntityId] = Initials(c.Name);
            return;
        }

        // Same job twice gets "Dragoon 1" and "Dragoon 2"; a lone job keeps its plain name.
        var byJob = encounter.Combatants.Values.Where(c => !c.IsAlly && !c.IsChocobo).OrderBy(c => c.EntityId).GroupBy(c => c.JobId);
        foreach (var group in byJob)
        {
            var job = GameData.JobInfo(group.Key);
            var label = mode == NameMode.JobShort ? job.Abbreviation : job.Name;
            if (label.Length == 0) label = "Player";

            var members = group.ToList();
            for (var i = 0; i < members.Count; i++)
                segmentNames[members[i].EntityId] = members.Count == 1 ? label : $"{label} {i + 1}";
        }
    }

    public static string Initials(string name)
    {
        var sb = new StringBuilder();
        foreach (var part in name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToUpperInvariant(part[0])).Append('.');
        }

        return sb.Length > 0 ? sb.ToString() : "?";
    }
}
