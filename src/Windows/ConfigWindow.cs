using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Aethermeter.Models;
using Aethermeter.Services;
using Aethermeter.UI;

namespace Aethermeter.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private int tab;

    public ConfigWindow(Plugin plugin) : base("Aethermeter Settings")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 600);
        SizeMin = new Vector2(440, 360);
    }

    private Configuration Config => plugin.Configuration;

    protected override string Title() => "Aethermeter";

    protected override void DrawContents()
    {
        Masthead();
        Theme.Tabs("##cfg", ref tab,
            (FontAwesomeIcon.Cog, "General"),
            (FontAwesomeIcon.PaintBrush, "Look"),
            (FontAwesomeIcon.UserSecret, "Streamer"),
            (FontAwesomeIcon.WindowRestore, "Windows"),
            (FontAwesomeIcon.FileAlt, "Logs"),
            (FontAwesomeIcon.InfoCircle, "About"));

        using var body = ImRaii.Child("##cfg-body", Vector2.Zero);
        using var width = ImRaii.ItemWidth(Math.Min(ImGui.GetContentRegionAvail().X * 0.5f, 240));
        var changed = tab switch
        {
            0 => General(),
            1 => Look(),
            2 => Streamer(),
            3 => WindowsTab(),
            4 => Logs(),
            _ => About(),
        };

        if (changed)
        {
            plugin.Names.Invalidate();
            plugin.SaveSoon();
        }
    }

    private void Masthead()
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        Theme.Logo(dl, p, 30);
        var font = plugin.Fonts.Get(FontId.BarlowCondensed);
        dl.AddText(font, 24f, p + new Vector2(40, 0), Theme.U32(Theme.Text), "AETHERMETER");
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.9f, p + new Vector2(41, 23), Theme.U32(Theme.TextMuted), $"v{Plugin.Version}  ·  damage & healing meter");

        var status = plugin.Hooks.IsActive ? "Recording" : "Hooks offline";
        var tint = plugin.Hooks.IsActive ? Theme.Aether : Theme.Ember;
        var sw = ImGui.CalcTextSize(status).X;
        var right = p.X + ImGui.GetContentRegionAvail().X;
        dl.AddCircleFilled(new Vector2(right - sw - 12, p.Y + 10), 3.5f, Theme.U32(tint));
        dl.AddText(new Vector2(right - sw, p.Y + 2), Theme.U32(tint), status);
        if (!plugin.Hooks.IsActive && plugin.Hooks.FailureReason is { } reason)
        {
            ImGui.SetCursorScreenPos(new Vector2(right - sw - 16, p.Y));
            ImGui.InvisibleButton("##status", new Vector2(sw + 16, 18));
            if (ImGui.IsItemHovered()) Theme.Tooltip(reason);
        }

        ImGui.SetCursorScreenPos(p + new Vector2(0, 44));
    }

    // ------------------------------------------------------------------

    private bool General()
    {
        var changed = false;

        Theme.Section("Tracking");
        changed |= Enum("Track", Config.TrackScope, v => Config.TrackScope = v,
            v => v == TrackScope.Party ? "Party (with pets and NPC allies)" : "Every player nearby");
        Theme.Help("\"Every player nearby\" is what you want in alliance raids, field operations and hunts.");

        var idle = Config.IdleTimeoutSeconds;
        if (ImGui.SliderFloat("End a pull after", ref idle, 5f, 60f, "%.0f s without hits"))
        {
            Config.IdleTimeoutSeconds = idle;
            changed = true;
        }

        Theme.Help("Pulls normally end when the party leaves combat; this is a safety net for fights that keep you flagged in combat.");

        var keep = Config.SegmentsToKeep;
        if (ImGui.SliderInt("Pulls to keep", ref keep, 1, 100))
        {
            Config.SegmentsToKeep = keep;
            changed = true;
        }

        changed |= Check("Save every pull to a file", Config.AutoSaveEncounters, v => Config.AutoSaveEncounters = v);
        ImGui.SameLine();
        if (ImGui.SmallButton("Open folder"))
        {
            System.IO.Directory.CreateDirectory(plugin.Exporter.Folder);
            Process.Start(new ProcessStartInfo { FileName = plugin.Exporter.Folder, UseShellExecute = true });
        }

        Theme.Section("Visibility");
        changed |= Check("Hide out of combat", Config.HideOutOfCombat, v => Config.HideOutOfCombat = v);
        if (Config.HideOutOfCombat)
        {
            using var indent = ImRaii.PushIndent();
            var linger = Config.LingerSeconds;
            if (ImGui.SliderFloat("Keep showing for", ref linger, 0f, 120f, "%.0f s after a pull"))
            {
                Config.LingerSeconds = linger;
                changed = true;
            }
        }

        changed |= Check("Only show in duties", Config.HideOutsideDuty, v => Config.HideOutsideDuty = v);
        changed |= Check("Hide during cutscenes", Config.HideInCutscenes, v => Config.HideInCutscenes = v);
        changed |= Check("Hide in group pose", Config.HideInGPose, v => Config.HideInGPose = v);
        changed |= Check("Hide when the game UI is hidden", Config.HideWithGameUi, v => Config.HideWithGameUi = v);

        return changed;
    }

    private bool Look()
    {
        var changed = false;

        Theme.Section("Bars");
        changed |= Enum("Style", Config.BarStyle, v => Config.BarStyle = v);
        changed |= Enum("Colour by", Config.BarColorMode, v => Config.BarColorMode = v);
        if (Config.BarColorMode == BarColorMode.Accent)
        {
            var color = Config.AccentColor;
            if (ImGui.ColorEdit4("Bar colour", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
            {
                Config.AccentColor = color;
                changed = true;
            }
        }

        changed |= Slider("Height", Config.BarHeight, 14f, 44f, "%.0f px", v => Config.BarHeight = v);
        changed |= Slider("Spacing", Config.BarSpacing, 0f, 10f, "%.0f px", v => Config.BarSpacing = v);
        changed |= Slider("Corner rounding", Config.Rounding, 0f, 10f, "%.0f px", v => Config.Rounding = v);
        changed |= Slider("Bar opacity", Config.BarOpacity, 0.2f, 1f, "%.2f", v => Config.BarOpacity = v);
        changed |= Slider("Background opacity", Config.BackgroundOpacity, 0f, 1f, "%.2f", v => Config.BackgroundOpacity = v);

        Theme.Section("Content");
        changed |= Check("Job icons", Config.ShowJobIcons, v => Config.ShowJobIcons = v);
        changed |= Check("Rank numbers", Config.ShowRank, v => Config.ShowRank = v);
        changed |= Check("Second figure (total next to DPS, DPS next to total)", Config.ShowSecondary, v => Config.ShowSecondary = v);
        changed |= Check("Share of the group", Config.ShowPercent, v => Config.ShowPercent = v);
        changed |= Check("Outline your own bar", Config.HighlightSelf, v => Config.HighlightSelf = v);
        changed |= Check("Group total footer", Config.ShowFooter, v => Config.ShowFooter = v);
        changed |= Enum("Numbers", Config.NumberFormat, v => Config.NumberFormat = v,
            v => v == NumberFormat.Short ? "Short (12.3k, 1.45M)" : "Full (12,345)");

        Theme.Section("Text");
        changed |= Font("Names", Config.NameFont, v => Config.NameFont = v);
        changed |= Slider("Name size", Config.NameFontSize, 10f, 28f, "%.0f px", v => Config.NameFontSize = v);
        changed |= Font("Numbers", Config.NumberFont, v => Config.NumberFont = v);
        changed |= Slider("Number size", Config.NumberFontSize, 10f, 30f, "%.0f px", v => Config.NumberFontSize = v);
        changed |= Font("Header", Config.HeaderFont, v => Config.HeaderFont = v);
        changed |= Slider("Header size", Config.HeaderFontSize, 11f, 26f, "%.0f px", v => Config.HeaderFontSize = v);
        changed |= Check("Text shadow", Config.TextShadow, v => Config.TextShadow = v);
        if (FontService.IsLatinOnly(Config.NameFont))
            ImGui.TextColored(Theme.Gold, "This name font can't draw Japanese, Chinese or Korean names.");

        Theme.Section("Motion");
        changed |= Enum("Reduce motion", Config.ReducedMotion, v => Config.ReducedMotion = v,
            v => v switch { ReducedMotionMode.System => "Follow Dalamud", ReducedMotionMode.On => "Always", _ => "Never" });

        return changed;
    }

    private bool Streamer()
    {
        var changed = false;

        Theme.Section("Player names");
        ImGui.TextWrapped("Hides other players' names everywhere Aethermeter shows them: bars, tooltips, breakdowns, death recaps, copied summaries and saved files.");
        ImGui.Dummy(new Vector2(0, 4));

        changed |= Enum("Show players as", Config.NameMode, v => Config.NameMode = v, v => v switch
        {
            NameMode.Full => "Their name",
            NameMode.Initials => "Initials (W. O.)",
            NameMode.Job => "Job (Dragoon 2)",
            _ => "Job abbreviation (DRG 2)",
        });

        changed |= Check("Show \"You\" for yourself", Config.ShowYou, v => Config.ShowYou = v);
        if (Config.NameMode != NameMode.Full && !Config.ShowYou)
            changed |= Check("Keep my own name", Config.KeepOwnName, v => Config.KeepOwnName = v);

        Theme.Section("Preview");
        var sample = "Warrior Of-Light";
        var preview = Config.NameMode switch
        {
            NameMode.Initials => Names.Initials(sample),
            NameMode.Job => "Paladin",
            NameMode.JobShort => "PLD",
            _ => sample,
        };
        ImGui.TextColored(Theme.TextMuted, $"{sample}  →  {preview}");

        return changed;
    }

    private bool WindowsTab()
    {
        var changed = false;
        Theme.Section("Meter windows");

        for (var i = 0; i < plugin.Meters.Count; i++)
        {
            var w = plugin.Meters[i].Settings;
            using var id = ImRaii.PushId(i);

            var open = w.Open;
            if (ImGui.Checkbox("##open", ref open))
            {
                w.Open = open;
                changed = true;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            changed |= Enum("##mode", w.Mode, v => w.Mode = v, MeterModes.Label);
            ImGui.SameLine();
            var locked = w.Locked;
            if (ImGui.Checkbox("Locked", ref locked))
            {
                w.Locked = locked;
                changed = true;
            }

            ImGui.SameLine();
            var through = w.ClickThrough;
            if (ImGui.Checkbox("Click-through", ref through))
            {
                w.ClickThrough = through;
                changed = true;
            }

            ImGui.SameLine();
            if (plugin.Meters.Count > 1 && Theme.IconButton("##del", FontAwesomeIcon.Times, "Remove this window", Theme.Ember))
            {
                plugin.CloseWindow(plugin.Meters[i]);
                return true;
            }
        }

        ImGui.Dummy(new Vector2(0, 4));
        if (ImGui.Button("Add a window")) plugin.AddWindow(MeterMode.Hps);
        ImGui.SameLine();
        if (ImGui.Button("Reset positions"))
        {
            for (var i = 0; i < plugin.Meters.Count; i++)
                plugin.Meters[i].MoveTo(new Vector2(60 + 30 * i, 320 + 30 * i));
            changed = true;
        }

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextColored(Theme.TextFaint, "Click-through only applies to locked windows. Unlock them here, or with /meter unlock.");
        return changed;
    }

    private bool confirmClean;

    private bool Logs()
    {
        var changed = false;
        var log = plugin.NetLog;

        ImGui.TextWrapped("Aethermeter can write the fight as an ACT network log, the format FFLogs reads. Upload it with the Archon App (FFLogs' uploader), no ACT needed.");

        Theme.Section("Live log");
        var live = Config.LiveLog;
        if (ImGui.Checkbox("Write a live log", ref live))
        {
            Config.LiveLog = live;
            changed = true;
        }

        Theme.Help("Also on the meter's header: the record dot. In the Archon App, choose live logging and point it at the folder below.");
        using (ImRaii.PushIndent())
        {
            changed |= Check("Only while in a duty", Config.LiveLogDutiesOnly, v => Config.LiveLogDutiesOnly = v);
            var (text, tint) = log.LiveState switch
            {
                Logging.LiveLogState.Writing => ($"Recording: {System.IO.Path.GetFileName(log.LivePath)} · {log.LiveLines:N0} lines", Theme.Ember),
                Logging.LiveLogState.Waiting => ("On, waiting for a duty to begin", Theme.Gold),
                _ => ("Off", Theme.TextMuted),
            };
            ImGui.TextColored(tint, text);
        }

        Theme.Section("Folder");
        ImGui.SetNextItemWidth(Math.Max(ImGui.GetContentRegionAvail().X - 120, 160));
        var folder = log.Folder;
        if (ImGui.InputText("##folder", ref folder, 512, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            Config.LogFolder = folder.Trim();
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Browse…"))
        {
            plugin.FileDialog.OpenFolderDialog("Folder for FFLogs logs", (ok, path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                Config.LogFolder = path;
                plugin.SaveSoon();
            }, log.Folder);
        }

        if (ImGui.SmallButton("Default folder"))
        {
            Config.LogFolder = string.Empty;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("ACT's folder (Archon default)"))
        {
            Config.LogFolder = Logging.NetworkLog.ActFolder;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Open"))
        {
            System.IO.Directory.CreateDirectory(log.Folder);
            plugin.OpenUrl(log.Folder);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Copy path")) ImGui.SetClipboardText(log.Folder);
        ImGui.TextColored(Theme.TextFaint, "Single pulls are saved in its \"Pulls\" subfolder. Files are named *_Aethermeter.log, so they never mix with ACT's.");

        Theme.Section("Single pulls");
        changed |= Check("Keep pulls ready for FFLogs", Config.KeepPullLogs, v => Config.KeepPullLogs = v);
        Theme.Help("Keeps this zone's log in memory, so any pull can be saved as its own small file from the meter's menu (\"Save pull log for FFLogs\"). Cleared when you change zone.");
        using (ImRaii.Disabled(!Config.KeepPullLogs))
        {
            changed |= Check("Save every pull automatically", Config.AutoSavePullLogs, v => Config.AutoSavePullLogs = v);
        }

        Theme.Section("Auto-clean");
        changed |= Enum("Delete logs older than", Config.LogRetention, v => Config.LogRetention = v, v => v switch
        {
            LogRetention.OneWeek => "1 week",
            LogRetention.TwoWeeks => "2 weeks",
            LogRetention.OneMonth => "1 month",
            LogRetention.ThreeMonths => "3 months",
            _ => "Never (keep everything)",
        });
        if (Config.LogRetention != LogRetention.Never)
        {
            using var indent = ImRaii.PushIndent();
            changed |= Check("Also delete saved segment files (.json)", Config.CleanSavedSegments, v => Config.CleanSavedSegments = v);
            var last = Config.LastLogCleanup == default ? "never" : Config.LastLogCleanup.ToString("g");
            ImGui.TextColored(Theme.TextMuted, $"Runs at load and every 6 hours. Last run: {last}.");
            if (ImGui.SmallButton("Clean now")) confirmClean = true;
        }

        if (confirmClean)
        {
            ImGui.OpenPopup("##clean-confirm");
            confirmClean = false;
        }

        using (var popup = ImRaii.Popup("##clean-confirm"))
        {
            if (popup)
            {
                var age = Logging.LogCleaner.Age(Config.LogRetention);
                ImGui.TextUnformatted($"Delete Aethermeter logs older than {age?.TotalDays:0} days now?");
                if (ImGui.Button("Delete"))
                {
                    var n = plugin.Cleaner.Run(plugin.NetLog.LivePath);
                    plugin.Notify(n == 0 ? "Nothing old enough to delete." : $"Deleted {n} old file(s).");
                    plugin.SaveSoon();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            }
        }

        Theme.Section("Uploading");
        ImGui.TextWrapped("Live: in the Archon App, start live logging and select the folder above. One pull: in the Archon App, choose \"Upload a log\" and pick the file from the Pulls folder.");
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.TextColored(Theme.Gold, "Experimental: the log follows ACT's documented format, but not yet confirmed against every FFLogs parser version. Please report any upload that fails.");
        if (!plugin.Hooks.DirectorActive)
            ImGui.TextColored(Theme.Ember, "Director hook offline: logs won't mark pull starts and wipes.");

        return changed;
    }

    private bool About()
    {
        Theme.Section("Aethermeter");
        ImGui.TextColored(Theme.TextMuted, $"Version {Plugin.Version}  ·  by GitHixy");
        ImGui.Dummy(new Vector2(0, 2));
        if (Theme.PillButton("What's new", Vector2.Zero, Theme.Aether, filled: true, icon: FontAwesomeIcon.Gift))
            plugin.ChangelogWindow.ShowAll();
        ImGui.SameLine(0, 8);
        Theme.CommunityButtons(plugin);
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.TextWrapped("Found a bug or have an idea? Come and talk about it on Discord, where new versions are announced first. If you enjoy Aethermeter, Patreon keeps it going.");

        Theme.Section("How it works");
        ImGui.TextWrapped("Aethermeter reads the combat results your game client already receives, through two game functions located by FFXIVClientStructs. Nothing is sent anywhere and nothing is posted to chat.");
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextWrapped("Overhealing is estimated from each target's HP when the heal lands, so it can differ slightly from FFLogs.");
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextWrapped("Shields count as healing by whoever cast them, for the damage they actually absorbed. The game only reports the shield gauge as a whole percent of max HP, so each shield is accurate to about 1% of the target's HP.");
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextWrapped("DoT and HoT ticks: when the server sends one tick per player, each is credited exactly. When it sends one combined tick for every DoT on a target, the total stays exact and is shared out by each DoT's learned per-tick value, as ACT and FFLogs also have to estimate.");

        Theme.Section("Commands");
        Command("/meter", "Show or hide the meter windows");
        Command("/meter config", "Open these settings");
        Command("/meter reset", "Clear every segment");
        Command("/meter lock | unlock", "Lock or unlock every window");
        Command("/meter new", "Add a meter window");
        Command("/meter test", "Play a made-up fight to preview the meter");
        Command("/meter log", "Start or stop the live log for FFLogs");
        Command("/meter news", "Show what's new");

        Theme.Section("Status");
        ImGui.TextColored(plugin.Hooks.IsActive ? Theme.Aether : Theme.Ember, plugin.Hooks.IsActive ? "Combat hook active" : "Combat hook offline: " + plugin.Hooks.FailureReason);
        ImGui.TextColored(plugin.Hooks.TicksActive ? Theme.Aether : Theme.Gold, plugin.Hooks.TicksActive ? "DoT/HoT tick hook active" : "DoT/HoT tick hook offline: ticks are not counted");
        if (plugin.Hooks.Errors > 0)
            ImGui.TextColored(Theme.Gold, $"{plugin.Hooks.Errors} event(s) could not be read; see /xllog.");
        return false;
    }

    private static void Command(string command, string text)
    {
        ImGui.TextColored(Theme.AetherBright, command);
        ImGui.SameLine(170);
        ImGui.TextColored(Theme.TextMuted, text);
    }

    // ------------------------------------------------------------------
    // Field helpers
    // ------------------------------------------------------------------

    private static bool Check(string label, bool value, Action<bool> set)
    {
        if (!ImGui.Checkbox(label, ref value)) return false;
        set(value);
        return true;
    }

    private static bool Slider(string label, float value, float min, float max, string format, Action<float> set)
    {
        if (!ImGui.SliderFloat(label, ref value, min, max, format)) return false;
        set(value);
        return true;
    }

    private static bool Enum<T>(string label, T value, Action<T> set, Func<T, string>? name = null) where T : struct, Enum
    {
        name ??= v => v.ToString();
        var changed = false;
        using var combo = ImRaii.Combo(label, name(value));
        if (!combo) return false;

        foreach (var option in System.Enum.GetValues<T>())
        {
            if (ImGui.Selectable(name(option), option.Equals(value)))
            {
                set(option);
                changed = true;
            }
        }

        return changed;
    }

    private bool Font(string label, FontId value, Action<FontId> set)
        => Enum(label, value, set, FontService.DisplayName);
}
