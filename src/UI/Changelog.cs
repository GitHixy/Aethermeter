using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Aethermeter.UI;

/// <summary>What a single release note is about, which drives its colour and tag.</summary>
public enum NoteKind
{
    Added,
    Changed,
    Improved,
    Fixed,
    Removed,
}

public readonly record struct Note(NoteKind Kind, string Text);

public readonly record struct Release(string Version, string Headline, Note[] Notes);

/// <summary>
/// The release notes shown after an update. Newest first: add an entry at the top whenever the
/// assembly version in Aethermeter.csproj changes.
/// </summary>
public static class Changelog
{
    public static readonly Release[] Releases =
    [
        new("0.1.2", "Into the arena",
        [
            new(NoteKind.Added, "PvP stats (/meter pvp, or the meter's menu): kills, deaths and assists for your side, with damage "
                                + "to players, healing, damage taken, the team's knockout score and a kill feed. Sort by any column, "
                                + "pick a past match from the header. It shows up on its own in PvP areas."),
            new(NoteKind.Added, "/meter test pvp plays a made-up five-on-five match, knockouts and respawns included."),
            new(NoteKind.Improved, "A PvP match is one segment from the first hit to the end of the match, instead of a new pull "
                                   + "after every respawn."),
            new(NoteKind.Fixed, "Opposing players were counted as allies with \"Every player nearby\"; they never are now."),
            new(NoteKind.Fixed, "A chocobo could show someone else's name as its rider, when its id had belonged to another "
                                + "player's bird before."),
        ]),
        new("0.1.1", "Party cooldowns, and a window that's yours",
        [
            new(NoteKind.Added, "Party cooldowns (/meter cd, or the meter's menu): raid buffs, party mitigation, healing cooldowns "
                                + "and tank invulnerabilities for everyone in your party, as a grid with countdowns or as a "
                                + "timeline of the pull. /meter test plays them too."),
            new(NoteKind.Added, "Five ready-made looks for the meter window: Obsidian, Midnight, Ember, Amethyst and Minimal."),
            new(NoteKind.Added, "Tune the window itself: background colour and style (solid, gradient, glass or glow), rounding, "
                                + "border colour and thickness, header colour and opacity, accent colour and text colour."),
            new(NoteKind.Added, "A faint \"Aethermeter by GitHixy\" watermark behind the bars, with its own opacity (or off)."),
            new(NoteKind.Fixed, "Logs for FFLogs now show damage and healing: each hit gets the effect-result line (37) FFLogs "
                                + "needs, line hashes follow ACT's recipe, and duties are named as in the Duty Finder."),
            new(NoteKind.Changed, "Logs say in their first line that Aethermeter wrote them. FFLogs reads them but marks them invalid "
                                  + "for rankings; use ACT or IINACT for ranked logs."),
            new(NoteKind.Improved, "The font pickers show every font in its own face."),
            new(NoteKind.Fixed, "The number font picker didn't open: it shared its ID with the number format picker."),
        ]),
        new("0.1.0", "Welcome to Aethermeter",
        [
            new(NoteKind.Added, "A damage and healing meter drawn by the game itself: no external parser to start."),
            new(NoteKind.Added, "Seven views: DPS, damage done, HPS, healing done, damage taken, overhealing and deaths."),
            new(NoteKind.Added, "Every pull kept as its own segment, plus an Overall segment. Pick any past pull from the header."),
            new(NoteKind.Added, "Click a bar for that player's breakdown: abilities, crit and direct-hit rates, damage taken, "
                                + "a DPS/HPS graph and a recap of the seconds before each death."),
            new(NoteKind.Added, "Shields count as healing by their caster, for the damage they actually absorbed."),
            new(NoteKind.Added, "DoT and HoT ticks are credited to whoever applied them, ground effects included; "
                                + "combined ticks are shared out by each DoT's learned value."),
            new(NoteKind.Added, "Chocobo companions get their own line, \"Bocchan (Kael Highwind)\", in chocobo yellow, with "
                                + "the chocobo icon from the party list."),
            new(NoteKind.Added, "Logs for FFLogs without ACT: a live log you switch on from the record dot on the meter's header "
                                + "(or only during duties), and any pull saved as its own file. Upload them with the Archon App."),
            new(NoteKind.Added, "Pick the folder logs go to, and let the auto-clean delete them after 1 week, 2 weeks, "
                                + "1 month or 3 months (Settings → Logs)."),
            new(NoteKind.Added, "Streamer mode: show other players by job, job abbreviation or initials, everywhere."),
            new(NoteKind.Added, "As many meter windows as you like, with four bar styles, fonts, colours and layout to tune."),
            new(NoteKind.Added, "/meter test plays a made-up fight so you can style the meter anywhere."),
        ]),
    ];

    /// <summary>The running assembly's version, formatted the way the notes list them.</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static Release? Latest => Releases.Length > 0 ? Releases[0] : null;

    /// <summary>
    /// Everything published since <paramref name="sinceVersion"/>. An unknown or missing version
    /// yields just the newest release, so an upgrade never dumps the whole history.
    /// </summary>
    public static IReadOnlyList<Release> Since(string sinceVersion)
    {
        if (!System.Version.TryParse(sinceVersion, out var seen))
            return Latest.HasValue ? [Latest.Value] : [];

        return Releases.Where(r => System.Version.TryParse(r.Version, out var v) && v > seen).ToList();
    }

    public static string KindLabel(NoteKind kind) => kind switch
    {
        NoteKind.Added => "NEW",
        NoteKind.Changed => "CHANGED",
        NoteKind.Improved => "IMPROVED",
        NoteKind.Fixed => "FIXED",
        NoteKind.Removed => "REMOVED",
        _ => "NOTE",
    };

    public static Vector4 KindColor(NoteKind kind) => kind switch
    {
        NoteKind.Added => Theme.Aether,
        NoteKind.Changed => Theme.TextMuted,
        NoteKind.Improved => Theme.Gold,
        NoteKind.Fixed => Theme.Ember,
        NoteKind.Removed => Theme.TextFaint,
        _ => Theme.TextMuted,
    };
}
