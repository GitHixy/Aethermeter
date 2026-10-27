using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Aethermeter.Combat;
using Aethermeter.Logging;
using Aethermeter.Models;
using Aethermeter.Services;
using Aethermeter.UI;

namespace Aethermeter.Windows;

/// <summary>
/// One meter: a header with the view and segment, ranked bars, and a footer with the group total.
/// Everything inside is drawn by hand on the draw list, so it scales with the configured fonts.
/// </summary>
public sealed class MeterWindow
{
    private readonly Plugin plugin;
    public MeterWindowConfig Settings { get; }

    /// <summary>Bars ease toward their value and slot instead of jumping.</summary>
    private sealed class RowAnim
    {
        public float Fill;
        public float Y = -1;
        public double Seen;
    }

    private readonly Dictionary<uint, RowAnim> anim = new();
    private Encounter? animFor;
    private float scroll;
    private bool confirmReset;
    private bool openModeMenu;
    private bool moveRequested;

    /// <summary>A past pull picked from the segment menu, shown until "Current" is chosen again.</summary>
    private int? fixedSegment;

    public MeterWindow(Plugin plugin, MeterWindowConfig settings)
    {
        this.plugin = plugin;
        Settings = settings;
    }

    private Configuration Config => plugin.Configuration;

    private float Radius => Chrome.Radius(Config);
    private Vector4 Accent => Config.WindowAccent;
    private Vector4 AccentBright => Chrome.AccentBright(Config);

    /// <summary>Half the watermark's height this frame, so the empty state can sit below it.</summary>
    private float watermarkHalf;

    public void MoveTo(Vector2 position)
    {
        Settings.Position = position;
        moveRequested = true;
    }

    private SegmentRef Segment => Settings.ShowOverall
        ? SegmentRef.Overall
        : fixedSegment is { } id ? new SegmentRef(SegmentKind.Fixed, id) : SegmentRef.Current;

    public void Draw()
    {
        if (!Settings.Open) return;

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;
        if (Settings.Locked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        if (Settings.Locked && Settings.ClickThrough) flags |= ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(Settings.Position, moveRequested ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        moveRequested = false;
        ImGui.SetNextWindowSize(Settings.Size, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(170, 70), new Vector2(2000, 2000));
        ImGui.SetNextWindowBgAlpha(Config.BackgroundOpacity);

        using var theme = Theme.Push();
        using var chrome = Chrome.Push(Config, new Vector2(170, 70));

        if (ImGui.Begin($"##aethermeter-{Settings.Id}", flags))
        {
            try
            {
                Track();
                DrawContents();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error drawing meter window");
            }
        }

        ImGui.End();
    }

    /// <summary>Remembers where the window was moved or resized to.</summary>
    private void Track()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (Vector2.DistanceSquared(pos, Settings.Position) > 0.25f || Vector2.DistanceSquared(size, Settings.Size) > 0.25f)
        {
            Settings.Position = pos;
            Settings.Size = size;
            plugin.SaveSoon();
        }
    }

    private void DrawContents()
    {
        var dl = ImGui.GetWindowDrawList();
        var winMin = ImGui.GetWindowPos();
        var winMax = winMin + ImGui.GetWindowSize();
        var now = plugin.Now;

        lock (plugin.Encounters.Sync)
        {
            var encounter = plugin.Encounters.Resolve(Segment);
            if (encounter is null && fixedSegment is not null)
            {
                fixedSegment = null;
                encounter = plugin.Encounters.Resolve(Segment);
            }

            var headerH = DrawHeader(dl, winMin, winMax.X, encounter, now);
            var footerH = Config.ShowFooter ? Config.NumberFontSize + 10f : 0f;

            var bodyMin = new Vector2(winMin.X, winMin.Y + headerH);
            var bodyMax = new Vector2(winMax.X, winMax.Y - footerH);
            Chrome.Backdrop(dl, Config, bodyMin, bodyMax);
            watermarkHalf = Chrome.Watermark(dl, plugin.Fonts.Get(FontId.BarlowCondensed), Config, bodyMin, bodyMax);
            ImGui.SetCursorScreenPos(bodyMin);
            using (ImRaii.Child("##bars", bodyMax - bodyMin, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground))
            {
                DrawBody(encounter, bodyMin, bodyMax, now);
            }

            if (footerH > 0)
                DrawFooter(dl, new Vector2(winMin.X, winMax.Y - footerH), winMax, encounter, now);
        }

        DrawPopups();
    }

    // ------------------------------------------------------------------
    // Header
    // ------------------------------------------------------------------

    private float DrawHeader(ImDrawListPtr dl, Vector2 min, float right, Encounter? encounter, double now)
    {
        var font = plugin.Fonts.Get(Config.HeaderFont);
        var size = Config.HeaderFontSize;
        var h = MathF.Round(size + 14f);
        var max = new Vector2(right, min.Y + h);
        Chrome.Header(dl, Config, min, max);

        var textY = min.Y + (h - size) * 0.5f;
        var x = min.X + 9;
        if (Config.ShowHeaderLogo)
        {
            var logo = h - 12f;
            Theme.Logo(dl, new Vector2(min.X + 7, min.Y + 6), logo, accent: Accent);
            x = min.X + 13 + logo;
        }

        // Right-hand cluster first, so the titles know how much room is left.
        var btn = h - 6f;
        var bx = right - 4 - btn;
        ImGui.SetCursorScreenPos(new Vector2(bx, min.Y + 3));
        if (Theme.IconButton("##menu", FontAwesomeIcon.Bars, "Menu", null, btn)) ImGui.OpenPopup("##meter-menu");
        bx -= btn;
        ImGui.SetCursorScreenPos(new Vector2(bx, min.Y + 3));
        if (Theme.IconButton("##reset", FontAwesomeIcon.Trash, "Reset all segments", null, btn)) confirmReset = true;
        bx -= btn;
        DrawLogButton(dl, new Vector2(bx, min.Y + 3), btn, now);

        var timer = encounter is null ? "--:--" : Format.Duration(encounter.Duration(now));
        var timerW = Theme.Measure(font, size, timer);
        var timerX = bx - 8 - timerW;
        var live = encounter is { Active: true, IsOverall: false } || encounter is { IsOverall: true } && plugin.Encounters.InCombat;
        Theme.Text2(dl, font, size, new Vector2(timerX, textY), Theme.U32(live ? AccentBright : Theme.TextMuted), timer, false);
        if (live)
        {
            // Breathing dot: the meter is recording.
            var pulse = plugin.ReducedMotion ? 1f : 0.55f + 0.45f * MathF.Sin((float)now * 4f);
            dl.AddCircleFilled(new Vector2(timerX - 7, textY + size * 0.55f), 3f, Theme.U32(Theme.Ember, pulse));
            timerX -= 12;
        }

        // Mode, then segment. Each opens its own menu.
        var mode = MeterModes.Label(Settings.Mode);
        var modeW = Theme.Measure(font, size, mode);
        var room = timerX - 10 - x;
        var modeFit = Theme.Fit(font, size, mode, Math.Min(modeW, room));
        ImGui.SetCursorScreenPos(new Vector2(x - 2, min.Y + 2));
        if (ImGui.InvisibleButton("##mode", new Vector2(Math.Max(Theme.Measure(font, size, modeFit) + 4, 1), h - 4)))
            ImGui.OpenPopup("##meter-mode");
        var modeHover = ImGui.IsItemHovered();
        Theme.Text2(dl, font, size, new Vector2(x, textY), Theme.U32(modeHover ? AccentBright : Config.TextColor), modeFit, false);
        x += Theme.Measure(font, size, modeFit) + 8;

        var segText = SegmentLabel(encounter);
        var segRoom = timerX - 10 - x;
        if (segRoom > 24)
        {
            var segFit = Theme.Fit(font, size * 0.86f, segText, segRoom);
            var segW = Theme.Measure(font, size * 0.86f, segFit);
            ImGui.SetCursorScreenPos(new Vector2(x - 2, min.Y + 2));
            if (ImGui.InvisibleButton("##segment", new Vector2(Math.Max(segW + 4, 1), h - 4)))
                ImGui.OpenPopup("##meter-segment");
            var segHover = ImGui.IsItemHovered();
            Theme.Text2(dl, font, size * 0.86f, new Vector2(x, textY + size * 0.08f), Theme.U32(segHover ? AccentBright : Theme.TextMuted), segFit, false);
        }

        return h;
    }

    /// <summary>
    /// The live log switch: a record dot. Grey when off, gold while waiting for a duty, and a
    /// breathing ember while lines are being written.
    /// </summary>
    private void DrawLogButton(ImDrawListPtr dl, Vector2 pos, float size, double now)
    {
        var state = plugin.NetLog.LiveState;
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton("##livelog", new Vector2(size, size))) plugin.ToggleLiveLog();
        var hovered = ImGui.IsItemHovered();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(size, size), Theme.U32(Theme.SurfaceHover), Theme.Radius);

        var center = pos + new Vector2(size, size) * 0.5f;
        var r = size * 0.22f;
        switch (state)
        {
            case LiveLogState.Writing:
                var pulse = plugin.ReducedMotion ? 1f : 0.6f + 0.4f * MathF.Sin((float)now * 3f);
                dl.AddCircleFilled(center, r * 1.9f, Theme.U32(Theme.Ember, 0.18f * pulse));
                dl.AddCircleFilled(center, r, Theme.U32(Theme.Ember));
                break;
            case LiveLogState.Waiting:
                dl.AddCircle(center, r, Theme.U32(Theme.Gold), 0, 1.6f);
                dl.AddCircleFilled(center, r * 0.45f, Theme.U32(Theme.Gold));
                break;
            default:
                dl.AddCircle(center, r, Theme.U32(hovered ? Theme.Text : Theme.TextMuted), 0, 1.4f);
                break;
        }

        if (!hovered) return;
        var tip = state switch
        {
            LiveLogState.Writing => $"Live log for FFLogs: recording\n{plugin.NetLog.LivePath}\n{plugin.NetLog.LiveLines:N0} lines\n\nClick to stop.",
            LiveLogState.Waiting => "Live log for FFLogs: on, waiting for a duty to begin.\n\nClick to turn off.",
            _ => "Live log for FFLogs: off.\n\nClick to start writing a log the Archon App can upload.",
        };
        Theme.Tooltip(tip);
    }

    private string SegmentLabel(Encounter? encounter)
    {
        if (Settings.ShowOverall) return "Overall";
        if (encounter is null) return "No combat yet";
        return fixedSegment is null ? encounter.Title : $"#{encounter.Id} {encounter.Title}";
    }

    // ------------------------------------------------------------------
    // Bars
    // ------------------------------------------------------------------

    private void DrawBody(Encounter? encounter, Vector2 min, Vector2 max, double now)
    {
        var dl = ImGui.GetWindowDrawList();
        var width = max.X - min.X;
        var rows = encounter is null ? new List<(Combatant C, double V)>() : Rows(encounter, now);

        if (!ReferenceEquals(animFor, encounter))
        {
            anim.Clear();
            animFor = encounter;
            scroll = 0;
        }

        if (rows.Count == 0)
        {
            DrawEmpty(dl, min, max, encounter);
            return;
        }

        var barH = MathF.Round(Config.BarHeight);
        var gap = Config.BarSpacing;
        var pad = 3f;
        var contentH = rows.Count * (barH + gap) + pad;
        var viewH = max.Y - min.Y;

        // Wheel scrolls the list when it's longer than the window.
        if (ImGui.IsWindowHovered() && contentH > viewH)
            scroll -= ImGui.GetIO().MouseWheel * (barH + gap) * 2;
        scroll = Math.Clamp(scroll, 0, Math.Max(contentH - viewH, 0));

        var dt = Math.Min(ImGui.GetIO().DeltaTime, 0.1f);
        var ease = plugin.ReducedMotion ? 1f : 1f - MathF.Exp(-dt * 11f);
        var top = rows[0].V;
        var seconds = encounter!.Duration(now);
        var shareTotal = rows.Sum(r => MeterModes.Total(Settings.Mode, r.C));

        var nameFont = plugin.Fonts.Get(Config.NameFont);
        var numFont = plugin.Fonts.Get(Config.NumberFont);

        for (var i = 0; i < rows.Count; i++)
        {
            var (c, value) = rows[i];
            if (!anim.TryGetValue(c.EntityId, out var a)) anim[c.EntityId] = a = new RowAnim();
            a.Seen = now;

            var targetY = i * (barH + gap);
            a.Y = a.Y < 0 ? targetY : a.Y + (targetY - a.Y) * ease;
            a.Fill += ((float)(top > 0 ? value / top : 0) - a.Fill) * ease;

            var y = min.Y + pad + a.Y - scroll;
            if (y > max.Y || y + barH < min.Y) continue;

            var rowMin = new Vector2(min.X + pad, y);
            var rowMax = new Vector2(min.X + width - pad, y + barH);

            ImGui.SetCursorScreenPos(rowMin);
            ImGui.InvisibleButton($"##row{c.EntityId}", rowMax - rowMin);
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                plugin.OpenBreakdown(Segment, c.EntityId, Settings.Mode);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                openModeMenu = true;

            DrawRow(dl, rowMin, rowMax, c, encounter, value, seconds, shareTotal, i + 1, a.Fill, hovered, nameFont, numFont);
            if (hovered) RowTooltip(c, encounter, seconds);
        }

        // Forget rows that dropped out of the list.
        foreach (var gone in anim.Where(p => p.Value.Seen < now).Select(p => p.Key).ToList())
            anim.Remove(gone);

        ImGui.SetCursorScreenPos(new Vector2(min.X, min.Y + contentH - scroll));
        ImGui.Dummy(Vector2.Zero);
    }

    private List<(Combatant C, double V)> Rows(Encounter encounter, double now)
    {
        var seconds = encounter.Duration(now);
        return encounter.Combatants.Values
            .Select(c => (C: c, V: MeterModes.Value(Settings.Mode, c, seconds)))
            .Where(r => r.V > 0)
            .OrderByDescending(r => r.V)
            .ThenBy(r => r.C.EntityId)
            .ToList();
    }

    private void DrawRow(ImDrawListPtr dl, Vector2 min, Vector2 max, Combatant c, Encounter encounter, double value, double seconds,
                         double shareTotal, int rank, float fill, bool hovered, ImFontPtr nameFont, ImFontPtr numFont)
    {
        var h = max.Y - min.Y;
        var rounding = Math.Min(Config.Rounding, h * 0.5f);
        var color = JobColors.For(c, Config);

        Bars.Track(dl, min, max, rounding, Config.BarOpacity);
        Bars.Fill(dl, min, max, fill, color, Config.BarStyle, rounding, Config.BarOpacity, hovered);

        if (Config.HighlightSelf && c.IsLocalPlayer)
            dl.AddRect(min, max, Theme.U32(AccentBright, 0.55f), rounding, ImDrawFlags.None, 1f);
        if (hovered)
            dl.AddRect(min, max, Theme.U32(Theme.Text, 0.35f), rounding, ImDrawFlags.None, 1f);

        var x = min.X + 4;
        if (Config.ShowJobIcons)
        {
            var job = GameData.Profile(c);
            if (job.Icon != 0)
            {
                var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.Icon)).GetWrapOrEmpty();
                var icon = h - 2;
                dl.AddImage(wrap.Handle, new Vector2(x - 2, min.Y + 1), new Vector2(x - 2 + icon, min.Y + 1 + icon));
                x += icon + 2;
            }
        }

        var shadow = Config.TextShadow;
        var textColor = Theme.U32(Config.TextColor);
        var nameSize = Math.Min(Config.NameFontSize, h - 2);
        var numSize = Math.Min(Config.NumberFontSize, h);
        var nameY = min.Y + (h - nameSize) * 0.5f - 0.5f;
        var numY = min.Y + (h - numSize) * 0.5f;

        if (Config.ShowRank)
        {
            var rankText = rank.ToString();
            Theme.Text2(dl, numFont, numSize * 0.92f, new Vector2(x, numY + numSize * 0.04f), Theme.U32(Config.TextColor, 0.75f), rankText, shadow);
            x += Math.Max(Theme.Measure(numFont, numSize * 0.92f, rankText), Theme.Measure(numFont, numSize * 0.92f, "0")) + 6;
        }

        // Right-aligned figures: main value, then (secondary, share) in a dimmer tone.
        var main = Settings.Mode == MeterMode.Deaths ? ((int)value).ToString() : Format.Number(value, Config.NumberFormat);
        var extra = Extra(c, seconds, shareTotal);
        var right = max.X - 6;

        if (extra.Length > 0)
        {
            var w = Theme.Measure(numFont, numSize * 0.86f, extra);
            right -= w;
            Theme.Text2(dl, numFont, numSize * 0.86f, new Vector2(right, numY + numSize * 0.08f), Theme.U32(Config.TextColor, 0.7f), extra, shadow);
            right -= 5;
        }

        var mainW = Theme.Measure(numFont, numSize, main);
        right -= mainW;
        Theme.Text2(dl, numFont, numSize, new Vector2(right, numY), textColor, main, shadow);

        var name = plugin.Names.For(c, encounter);
        var fitted = Theme.Fit(nameFont, nameSize, name, right - 8 - x);
        Theme.Text2(dl, nameFont, nameSize, new Vector2(x, nameY), textColor, fitted, shadow);
    }

    private string Extra(Combatant c, double seconds, double shareTotal)
    {
        var parts = new List<string>(2);
        if (Config.ShowSecondary)
        {
            var companion = MeterModes.Companion(Settings.Mode);
            if (companion is { } m)
                parts.Add(Format.Number(MeterModes.Value(m, c, seconds), Config.NumberFormat));
        }

        if (Config.ShowPercent && Settings.Mode != MeterMode.Deaths && shareTotal > 0)
            parts.Add(Format.Percent(MeterModes.Total(Settings.Mode, c) / shareTotal));

        return parts.Count == 0 ? string.Empty : "(" + string.Join(", ", parts) + ")";
    }

    private void RowTooltip(Combatant c, Encounter encounter, double seconds)
    {
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        ImGui.BeginTooltip();

        var job = GameData.Profile(c);
        ImGui.TextColored(JobColors.For(c, Config), plugin.Names.For(c, encounter));
        if (job.Name.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, job.Name);
        }

        var healing = MeterModes.IsHealing(Settings.Mode);
        var taken = Settings.Mode == MeterMode.DamageTaken;
        IEnumerable<AbilityStats> list = taken ? c.TakenAbilities.Values : healing ? c.HealAbilities.Values.AsEnumerable() : c.DamageAbilities.Values;
        var total = taken ? c.DamageTaken : healing ? c.Healing + c.Overheal : c.Damage;
        var top = list.Where(a => a.Total > 0).OrderByDescending(a => a.Total).Take(6).ToList();

        if (top.Count > 0)
        {
            ImGui.Separator();
            if (ImGui.BeginTable("##tip", 3, ImGuiTableFlags.SizingFixedFit))
            {
                foreach (var a in top)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var label = GameData.AbilityName(a.ActionId, a.StatusId, a.IsShield);
                    if (a.PetName is not null) label += $" ({a.PetName})";
                    if (a.SourceName is not null) label += $" — {plugin.Names.Mask(a.SourceName, encounter)}";
                    ImGui.TextUnformatted(label);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Format.Number(a.Total, Config.NumberFormat));
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Theme.TextMuted, total > 0 ? Format.Percent((double)a.Total / total) : "-");
                }

                ImGui.EndTable();
            }
        }

        ImGui.Separator();
        if (healing)
            ImGui.TextColored(Theme.TextMuted, $"Overheal {Format.Percent(c.OverhealRate)}   ·   Shields {Format.Number(c.Absorbed, Config.NumberFormat)}   ·   Crit {Format.Percent(c.Heals > 0 ? (double)c.HealCrits / c.Heals : 0)}");
        else if (!taken)
            ImGui.TextColored(Theme.TextMuted, $"Crit {Format.Percent(c.CritRate)}   ·   DH {Format.Percent(c.DirectHitRate)}   ·   Max {Format.Number(c.MaxHit, Config.NumberFormat)}");
        if (c.Deaths.Count > 0)
            ImGui.TextColored(Theme.Ember, c.Deaths.Count == 1 ? "1 death" : $"{c.Deaths.Count} deaths");
        ImGui.TextColored(Theme.TextFaint, "Click for the full breakdown");

        ImGui.EndTooltip();
    }

    private void DrawEmpty(ImDrawListPtr dl, Vector2 min, Vector2 max, Encounter? encounter)
    {
        var font = plugin.Fonts.Get(Config.HeaderFont);
        var size = Config.HeaderFontSize * 0.9f;
        var text = encounter is null ? "Waiting for combat" : "Nothing to show in this view";
        var w = Theme.Measure(font, size, text);
        var center = (min + max) * 0.5f;
        var y = center.Y;
        if (watermarkHalf > 0)
            y = Math.Min(center.Y + watermarkHalf + 8f, max.Y - size - 4f); // under the watermark, which stands in for the logo
        else if (max.Y - min.Y > 60)
            Theme.Logo(dl, new Vector2(center.X - 11, center.Y - 30), 22, 0.45f, Accent);
        dl.AddText(font, size, new Vector2(center.X - w * 0.5f, y), Theme.U32(Theme.TextFaint), text);

        // Right-click anywhere still opens the view menu.
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##empty", Vector2.Max(max - min, Vector2.One));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            openModeMenu = true;
    }

    // ------------------------------------------------------------------
    // Footer
    // ------------------------------------------------------------------

    private void DrawFooter(ImDrawListPtr dl, Vector2 min, Vector2 max, Encounter? encounter, double now)
    {
        dl.AddRectFilled(min, max, Theme.U32(Config.HeaderColor, Config.HeaderOpacity * 0.82f), Radius, ImDrawFlags.RoundCornersBottom);
        dl.AddLine(min, new Vector2(max.X, min.Y), Theme.U32(Config.BorderColor), 1f);
        if (encounter is null) return;

        var font = plugin.Fonts.Get(Config.NumberFont);
        var size = Config.NumberFontSize * 0.82f;
        var y = min.Y + (max.Y - min.Y - size) * 0.5f;
        var seconds = encounter.Duration(now);

        double total = 0;
        foreach (var c in encounter.Combatants.Values)
            total += MeterModes.Value(Settings.Mode, c, seconds);

        var label = Settings.Mode switch
        {
            MeterMode.Deaths => "Deaths",
            MeterMode.DamageTaken => "Taken",
            _ when MeterModes.IsRate(Settings.Mode) => "Group " + MeterModes.Label(Settings.Mode),
            _ => "Group total",
        };
        dl.AddText(font, size, new Vector2(min.X + 8, y), Theme.U32(Theme.TextMuted), label.ToUpperInvariant());

        var value = Settings.Mode == MeterMode.Deaths ? ((int)total).ToString() : Format.Number(total, Config.NumberFormat);
        var w = Theme.Measure(font, size, value);
        dl.AddText(font, size, new Vector2(max.X - 8 - w, y), Theme.U32(AccentBright), value);
    }

    // ------------------------------------------------------------------
    // Menus
    // ------------------------------------------------------------------

    private void DrawPopups()
    {
        // Rows live in a child window, whose ID stack differs: their menu is opened from here.
        if (openModeMenu)
        {
            ImGui.OpenPopup("##meter-mode");
            openModeMenu = false;
        }

        if (confirmReset)
        {
            ImGui.OpenPopup("##meter-reset");
            confirmReset = false;
        }

        using (var popup = ImRaii.Popup("##meter-mode"))
        {
            if (popup)
            {
                ImGui.TextColored(Theme.TextFaint, "VIEW");
                foreach (var mode in MeterModes.All)
                {
                    if (ImGui.Selectable(MeterModes.Label(mode), Settings.Mode == mode))
                    {
                        Settings.Mode = mode;
                        plugin.SaveSoon();
                    }
                }
            }
        }

        using (var popup = ImRaii.Popup("##meter-segment"))
        {
            if (popup) DrawSegmentMenu();
        }

        using (var popup = ImRaii.Popup("##meter-menu"))
        {
            if (popup) DrawMainMenu();
        }

        using (var popup = ImRaii.Popup("##meter-reset"))
        {
            if (popup)
            {
                ImGui.TextUnformatted("Clear every segment, including Overall?");
                ImGui.Dummy(new Vector2(0, 2));
                using (ImRaii.PushColor(ImGuiCol.Button, Theme.Alpha(Theme.Ember, 0.35f)).Push(ImGuiCol.ButtonHovered, Theme.Alpha(Theme.Ember, 0.55f)))
                {
                    if (ImGui.Button("Reset"))
                    {
                        plugin.ResetAll();
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            }
        }
    }

    private void DrawSegmentMenu()
    {
        ImGui.TextColored(Theme.TextFaint, "SEGMENT");
        var encounters = plugin.Encounters;
        var now = plugin.Now;

        lock (encounters.Sync)
        {
            var current = encounters.Active is not null ? $"Current — {encounters.Active.Title} (live)" : "Current";
            if (ImGui.Selectable(current, !Settings.ShowOverall && fixedSegment is null))
            {
                Settings.ShowOverall = false;
                fixedSegment = null;
                plugin.SaveSoon();
            }

            if (ImGui.Selectable($"Overall — {Format.Duration(encounters.Overall.Duration(now))}", Settings.ShowOverall))
            {
                Settings.ShowOverall = true;
                fixedSegment = null;
                plugin.SaveSoon();
            }

            if (encounters.History.Count > 0) ImGui.Separator();
            foreach (var e in encounters.History)
            {
                var label = $"#{e.Id}  {e.Title}  ·  {Format.Duration(e.Duration(now))}  ·  {e.StartedAt:HH:mm}";
                if (ImGui.Selectable(label + $"##seg{e.Id}", !Settings.ShowOverall && fixedSegment == e.Id))
                {
                    Settings.ShowOverall = false;
                    fixedSegment = e.Id;
                }
            }
        }
    }

    private void DrawMainMenu()
    {
        var locked = Settings.Locked;
        if (ImGui.MenuItem("Lock window", string.Empty, ref locked))
        {
            Settings.Locked = locked;
            plugin.SaveSoon();
        }

        var through = Settings.ClickThrough;
        if (ImGui.MenuItem("Click-through when locked", string.Empty, ref through))
        {
            Settings.ClickThrough = through;
            plugin.SaveSoon();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Copy summary"))
        {
            lock (plugin.Encounters.Sync)
            {
                var e = plugin.Encounters.Resolve(Segment);
                if (e is not null)
                {
                    ImGui.SetClipboardText(plugin.Exporter.Summary(e, Settings.Mode, plugin.Now));
                    plugin.Notify("Summary copied to the clipboard.");
                }
            }
        }

        if (ImGui.MenuItem("Save segment to file"))
            plugin.SaveSegment(Segment);
        if (ImGui.MenuItem("Save pull log for FFLogs", string.Empty, false, !Settings.ShowOverall && plugin.Configuration.KeepPullLogs))
            plugin.SavePullLog(Segment);
        var live = plugin.Configuration.LiveLog;
        if (ImGui.MenuItem("Live log for FFLogs", string.Empty, ref live))
            plugin.ToggleLiveLog();

        ImGui.Separator();
        if (ImGui.MenuItem("New window")) plugin.AddWindow(Settings.Mode == MeterMode.Dps ? MeterMode.Hps : MeterMode.Dps);
        if (ImGui.MenuItem("Party cooldowns", string.Empty, Config.Cooldowns.Open)) plugin.ToggleCooldowns();
        if (ImGui.MenuItem("PvP stats", string.Empty, Config.Pvp.Open)) plugin.TogglePvp();
        if (ImGui.MenuItem("Close this window")) plugin.CloseWindow(this);
        if (ImGui.MenuItem("Settings")) plugin.ConfigWindow.IsOpen = true;
        if (ImGui.MenuItem("What's new")) plugin.ChangelogWindow.ShowAll();
    }
}
