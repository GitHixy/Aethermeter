using System;
using System.IO;
using System.Linq;
using System.Text;
using Aethermeter.Combat;
using Aethermeter.Models;
using Aethermeter.UI;
using Newtonsoft.Json;

namespace Aethermeter.Services;

/// <summary>
/// Gets a segment out of the game: a plain-text table for the clipboard, or a JSON file in the
/// encounters folder. Both go through <see cref="Names"/>, so streamer mode holds here too.
/// </summary>
public sealed class Exporter
{
    private readonly Configuration config;
    private readonly Names names;

    public string Folder { get; }

    public Exporter(Configuration config, Names names, string configDirectory)
    {
        this.config = config;
        this.names = names;
        Folder = Path.Combine(configDirectory, "encounters");
    }

    public string Summary(Encounter e, MeterMode mode, double now)
    {
        var seconds = e.Duration(now);
        var sb = new StringBuilder();
        sb.Append(e.Title).Append(" — ").Append(MeterModes.Label(mode)).Append(" — ").AppendLine(Format.Duration(seconds));

        var rows = e.Combatants.Values
            .Select(c => (c, v: MeterModes.Value(mode, c, seconds)))
            .Where(r => r.v > 0)
            .OrderByDescending(r => r.v)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            var (c, v) = rows[i];
            var job = GameData.Profile(c).Abbreviation;
            sb.Append(i + 1).Append(". ").Append(names.For(c, e));
            if (job.Length > 0) sb.Append(" (").Append(job).Append(')');
            sb.Append("  ").AppendLine(Format.Number(v, config.NumberFormat));
        }

        return sb.ToString();
    }

    /// <summary>Writes the segment as JSON and returns the file's path.</summary>
    public string Save(Encounter e, double now)
    {
        Directory.CreateDirectory(Folder);
        var seconds = e.Duration(now);
        var data = new
        {
            e.Title,
            e.Zone,
            Started = e.StartedAt,
            DurationSeconds = Math.Round(seconds, 2),
            e.TotalDamage,
            e.TotalHealing,
            e.TotalTaken,
            e.IsPvp,
            Knockouts = e.IsPvp ? e.Kills.Count(k => k.Ours) : (int?)null,
            KnockedOut = e.IsPvp ? e.Kills.Count(k => !k.Ours) : (int?)null,
            Combatants = e.Combatants.Values.OrderByDescending(c => c.Damage).Select(c => new
            {
                Name = names.For(c, e),
                Job = GameData.Profile(c).Abbreviation,
                Kills = e.IsPvp ? c.Kills : (int?)null,
                Assists = e.IsPvp ? c.Assists : (int?)null,
                PlayerDamage = e.IsPvp ? c.PlayerDamage : (long?)null,
                c.Damage,
                Dps = Math.Round(c.Damage / Math.Max(seconds, 1), 1),
                c.Healing,
                Hps = Math.Round(c.Healing / Math.Max(seconds, 1), 1),
                c.Overheal,
                c.Absorbed,
                c.DamageTaken,
                Deaths = c.Deaths.Count,
                CritRate = Math.Round(c.CritRate, 4),
                DirectHitRate = Math.Round(c.DirectHitRate, 4),
                c.MaxHit,
                Abilities = c.DamageAbilities.Values.Concat(c.HealAbilities.Values).OrderByDescending(a => a.Total).Select(a => new
                {
                    Name = GameData.AbilityName(a.ActionId, a.StatusId, a.IsShield),
                    a.IsShield,
                    a.PetName,
                    a.ActionId,
                    a.StatusId,
                    a.Total,
                    a.Overheal,
                    a.Hits,
                    a.Crits,
                    a.DirectHits,
                    a.Misses,
                    a.Max,
                }),
            }),
        };

        var safeTitle = string.Concat(e.Title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(Folder, $"{e.StartedAt:yyyyMMdd-HHmmss} {safeTitle}.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        return path;
    }
}
