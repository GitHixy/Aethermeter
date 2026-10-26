using System;
using System.Collections.Generic;
using System.IO;
using Aethermeter.Models;

namespace Aethermeter.Logging;

/// <summary>
/// Deletes old files Aethermeter wrote: its own logs (always named "*_Aethermeter.log", so ACT's
/// logs in a shared folder are never touched) and, if asked, saved segment files.
/// Runs on load and every few hours, and on demand from the settings.
/// </summary>
public sealed class LogCleaner
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly Configuration config;
    private readonly Func<IEnumerable<string>> logFolders;
    private readonly string encounterFolder;
    private DateTime nextRun = DateTime.MinValue;

    public LogCleaner(Configuration config, Func<IEnumerable<string>> logFolders, string encounterFolder)
    {
        this.config = config;
        this.logFolders = logFolders;
        this.encounterFolder = encounterFolder;
    }

    public static TimeSpan? Age(LogRetention retention) => retention switch
    {
        LogRetention.OneWeek => TimeSpan.FromDays(7),
        LogRetention.TwoWeeks => TimeSpan.FromDays(14),
        LogRetention.OneMonth => TimeSpan.FromDays(30),
        LogRetention.ThreeMonths => TimeSpan.FromDays(90),
        _ => null,
    };

    public DateTime? NextRun => Age(config.LogRetention) is null ? null : config.LastLogCleanup + Interval;

    /// <summary>Per frame: runs the cleanup when it's due.</summary>
    public int? Tick(string? fileInUse)
    {
        if (Age(config.LogRetention) is null || DateTime.Now < nextRun) return null;
        nextRun = DateTime.Now + Interval;
        return Run(fileInUse);
    }

    /// <summary>Deletes every file past the retention age and returns how many went.</summary>
    public int Run(string? fileInUse)
    {
        var age = Age(config.LogRetention);
        if (age is null) return 0;

        var cutoff = DateTime.Now - age.Value;
        var deleted = 0;

        foreach (var folder in logFolders())
            deleted += Sweep(folder, "*_Aethermeter.log", cutoff, fileInUse);
        if (config.CleanSavedSegments)
            deleted += Sweep(encounterFolder, "*.json", cutoff, fileInUse);

        config.LastLogCleanup = DateTime.Now;
        if (deleted > 0) Plugin.Log.Information("Auto-clean removed {Count} old file(s)", deleted);
        return deleted;
    }

    private static int Sweep(string folder, string pattern, DateTime cutoff, string? fileInUse)
    {
        if (!Directory.Exists(folder)) return 0;

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (string.Equals(file, fileInUse, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.GetLastWriteTime(file) >= cutoff) continue;
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Could not delete {File}", file);
            }
        }

        return deleted;
    }
}
