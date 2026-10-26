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
