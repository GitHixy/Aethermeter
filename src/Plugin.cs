using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Aethermeter.Combat;
using Aethermeter.Models;
using Aethermeter.Services;
using Aethermeter.Windows;

namespace Aethermeter;

public sealed class Plugin : IDalamudPlugin
{
    public static string Version => UI.Changelog.CurrentVersion;

    public const string PatreonUrl = "https://www.patreon.com/GitHixy";
    public const string DiscordUrl = "https://discord.gg/9kjUjWDBkJ";
    public const string RepoUrl = "https://github.com/GitHixy/Aethermeter";

    private const string CommandName = "/meter";
    private const string CommandAlt = "/aethermeter";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IPartyList PartyList { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IDutyState DutyState { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    public ConfigurationService ConfigService { get; }
    public Configuration Configuration => ConfigService.Configuration;
    public FontService Fonts { get; }
    public ActorResolver Actors { get; }
    public EncounterManager Encounters { get; }
    public CombatHooks Hooks { get; }
    public EventEmitter Emitter { get; }
    public TickAttributor Ticks { get; }
    public ShieldTracker Shields { get; }
    public Names Names { get; }
    public Exporter Exporter { get; }
    public TestFight Test { get; }
    public Logging.NetworkLog NetLog { get; }
    public Logging.LogCleaner Cleaner { get; }
    public Dalamud.Interface.ImGuiFileDialog.FileDialogManager FileDialog { get; } = new();

    public List<MeterWindow> Meters { get; } = new();
    public BreakdownWindow BreakdownWindow { get; }
    public ConfigWindow ConfigWindow { get; }
    public ChangelogWindow ChangelogWindow { get; }

    private readonly Stopwatch clock = Stopwatch.StartNew();
    public double Now => clock.Elapsed.TotalSeconds;

    /// <summary>When the last pull ended, for "hide out of combat" lingering.</summary>
    private double lastPullEnded = double.NegativeInfinity;
    private double saveAt = -1;
    private int frameErrors;

    public Plugin()
    {
        ConfigService = new ConfigurationService();
        Fonts = new FontService();
        Names = new Names(Configuration);
        Exporter = new Exporter(Configuration, Names, ConfigService.Directory);

        Actors = new ActorResolver(Configuration);
        Encounters = new EncounterManager(Configuration, Actors, () => Now);
        Encounters.Ended += OnPullEnded;
        Emitter = new EventEmitter(Encounters.Ingest);
        Ticks = new TickAttributor(Actors, Emitter);
        Shields = new ShieldTracker(Actors, Emitter);
        Emitter.Shields = Shields;
        Hooks = new CombatHooks(Actors, () => Now, Emitter, Ticks);
        Test = new TestFight(Encounters);
        NetLog = new Logging.NetworkLog(Configuration, () => Now, () => Encounters.InCombat);
        Hooks.Log = NetLog;
        Cleaner = new Logging.LogCleaner(Configuration, () => [NetLog.Folder, NetLog.PullFolder], Exporter.Folder);

        BreakdownWindow = new BreakdownWindow(this);
        ConfigWindow = new ConfigWindow(this);
        ChangelogWindow = new ChangelogWindow(this);

        if (Configuration.Windows.Count == 0)
            Configuration.Windows.Add(new MeterWindowConfig());
        foreach (var w in Configuration.Windows)
            Meters.Add(new MeterWindow(this, w));

        if (!Hooks.Enable())
            Notify("Aethermeter couldn't attach to the game's combat events. See /meter config > About.", NotificationType.Error);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Show or hide the meter\n/meter config → settings\n/meter reset → clear every segment\n/meter lock | unlock → lock or unlock every window\n/meter new → add a meter window\n/meter test → play a made-up fight to preview the meter\n/meter log → start or stop the live log for FFLogs\n/meter news → what's new",
        });
        CommandManager.AddHandler(CommandAlt, new CommandInfo(OnCommand) { ShowInHelp = false });

        Framework.Update += OnUpdate;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMeters;

        ShowChangelogIfUpdated();
    }

    /// <summary>After an update, the notes since the last version seen; a first install starts quiet.</summary>
    private void ShowChangelogIfUpdated()
    {
        if (ConfigService.IsNewConfiguration)
        {
            Configuration.LastSeenVersion = Version;
            ConfigService.Save();
            return;
        }

        if (Configuration.LastSeenVersion != Version)
            ChangelogWindow.ShowSince(Configuration.LastSeenVersion);
    }

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open {Url}", url);
        }
    }

    public void Dispose()
    {
        // Hooks first, so no event lands in a half-disposed plugin.
        Hooks.Dispose();
        NetLog.Dispose();

        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMeters;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlt);

        Encounters.Ended -= OnPullEnded;
        Encounters.Dispose();
        BreakdownWindow.Dispose();
        ConfigWindow.Dispose();
        ChangelogWindow.Dispose();
        Fonts.Dispose();
        ConfigService.Dispose();
    }

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
            case "settings":
                OpenConfig();
                break;
            case "reset":
                ResetAll();
                break;
            case "lock":
            case "unlock":
                var locked = args.Trim().Equals("lock", StringComparison.OrdinalIgnoreCase);
                foreach (var m in Meters) m.Settings.Locked = locked;
                SaveSoon();
                Notify(locked ? "Meter windows locked." : "Meter windows unlocked.");
                break;
            case "log":
                ToggleLiveLog();
                break;
            case "news":
            case "changelog":
                ChangelogWindow.ShowAll();
                break;
            case "new":
                AddWindow(MeterMode.Hps);
                break;
            case "test":
                if (Test.Running)
                    Test.Stop();
                else if (Encounters.InCombat)
                    Notify("Finish your current pull before starting a test fight.");
                else
                    Test.Start(Now);
                break;
            default:
                ToggleMeters();
                break;
        }
    }

    private void OpenConfig() => ConfigWindow.IsOpen = true;

    private void ToggleMeters()
    {
        var show = !Meters.Any(m => m.Settings.Open);
        foreach (var m in Meters) m.Settings.Open = show;
        SaveSoon();
    }

    // ------------------------------------------------------------------
    // Actions used by the windows
    // ------------------------------------------------------------------

    public void OpenBreakdown(SegmentRef segment, uint entityId, MeterMode mode) => BreakdownWindow.Show(segment, entityId, mode);

    public void ResetAll()
    {
        Encounters.Reset();
        Emitter.Clear();
        Ticks.Clear();
        Shields.Clear();
        Names.Invalidate();
    }

    public void AddWindow(MeterMode mode)
    {
        var last = Meters.LastOrDefault()?.Settings;
        var settings = new MeterWindowConfig
        {
            Mode = mode,
            Size = last?.Size ?? new MeterWindowConfig().Size,
            Position = last is null ? new MeterWindowConfig().Position : last.Position + new System.Numerics.Vector2(last.Size.X + 8, 0),
        };
        Configuration.Windows.Add(settings);
        Meters.Add(new MeterWindow(this, settings));
        SaveSoon();
    }

    public void CloseWindow(MeterWindow window)
    {
        // The last window is only hidden, so /meter can bring it back.
        if (Meters.Count <= 1)
            window.Settings.Open = false;
        else
        {
            Meters.Remove(window);
            Configuration.Windows.Remove(window.Settings);
        }

        SaveSoon();
    }

    public void SaveSegment(SegmentRef segment)
    {
        try
        {
            string? path;
            lock (Encounters.Sync)
            {
                var e = Encounters.Resolve(segment);
                path = e is null ? null : Exporter.Save(e, Now);
            }

            if (path is not null) Notify($"Saved to {path}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not save the segment");
            Notify("Could not save the segment; see /xllog.", NotificationType.Error);
        }
    }

    /// <summary>The live log switch on the meter header.</summary>
    public void ToggleLiveLog()
    {
        Configuration.LiveLog = !Configuration.LiveLog;
        SaveSoon();
        Notify(Configuration.LiveLog
            ? Configuration.LiveLogDutiesOnly ? "Live log on: it starts writing when a duty begins." : "Live log on."
            : "Live log off.");
    }

    /// <summary>Writes one pull as its own log file for FFLogs.</summary>
    public void SavePullLog(SegmentRef segment)
    {
        try
        {
            string? path = null;
            lock (Encounters.Sync)
            {
                var e = Encounters.Resolve(segment);
                if (e is null || !NetLog.CanSave(e))
                {
                    Notify("This pull's log isn't in memory any more (logs are kept for the current zone, with \"Keep pulls ready for FFLogs\" on).", NotificationType.Warning);
                    return;
                }

                path = NetLog.SavePull(e);
            }

            Notify($"Pull log saved to {path}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not save the pull log");
            Notify("Could not save the pull log; see /xllog.", NotificationType.Error);
        }
    }

    public void Notify(string text, NotificationType type = NotificationType.Info)
        => NotificationManager.AddNotification(new Notification { Title = "Aethermeter", Content = text, Type = type });

    /// <summary>Settings change in bursts while dragging; write them once things settle.</summary>
    public void SaveSoon() => saveAt = Now + 1.5;

    public bool ReducedMotion => Configuration.ReducedMotion switch
    {
        ReducedMotionMode.On => true,
        ReducedMotionMode.Off => false,
        _ => PluginInterface.UiBuilder.ShouldUseReducedMotion,
    };

    private void OnPullEnded(Encounter pull)
    {
        lastPullEnded = Now;
        NetLog.QueueSave(pull);
        if (!Configuration.AutoSaveEncounters) return;

        try
        {
            Exporter.Save(pull, Now);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not auto-save the pull");
        }
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Test.Update(Now);

            // Ticks of this server tick first, so the damage they did is known before shields are read.
            Ticks.Flush();
            Shields.Update(Encounters.TrackedIds(), Now);
            Encounters.Update();
            NetLog.Update();

            if (Cleaner.Tick(NetLog.LivePath) is > 0 and var removed)
                Notify($"Auto-clean removed {removed} old log file(s).");

            if (saveAt >= 0 && Now >= saveAt)
            {
                saveAt = -1;
                ConfigService.Save();
            }
        }
        catch (Exception ex)
        {
            if (frameErrors++ < 5) Log.Error(ex, "Error during framework update");
        }
    }

    /// <summary>Whether the meters should be on screen right now.</summary>
    private bool MetersVisible()
    {
        var c = Configuration;
        if (!ClientState.IsLoggedIn) return false;
        if (c.HideWithGameUi && GameGui.GameUiHidden) return false;
        if (c.HideInGPose && ClientState.IsGPosing) return false;
        if (c.HideInCutscenes && (Condition[ConditionFlag.OccupiedInCutSceneEvent] || Condition[ConditionFlag.WatchingCutscene] || Condition[ConditionFlag.WatchingCutscene78]))
            return false;

        // Settings open: show the meter so changes can be seen.
        if (ConfigWindow.IsOpen) return true;

        if (c.HideOutsideDuty && !Condition[ConditionFlag.BoundByDuty]) return false;
        if (c.HideOutOfCombat && !Encounters.InCombat && Now - lastPullEnded > c.LingerSeconds) return false;
        return true;
    }

    private void Draw()
    {
        Fonts.BeginFrame();
        try
        {
            if (MetersVisible())
            {
                // Copy: a window may close itself from its own menu.
                foreach (var meter in Meters.ToArray())
                    meter.Draw();
            }

            BreakdownWindow.Draw();
            ConfigWindow.Draw();
            ChangelogWindow.Draw();
            FileDialog.Draw();
        }
        catch (Exception ex)
        {
            if (frameErrors++ < 5) Log.Error(ex, "Error during frame");
        }
        finally
        {
            Fonts.EndFrame();
        }
    }
}
