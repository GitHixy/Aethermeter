using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Aethermeter.Combat;
using Aethermeter.Models;
using Aethermeter.Services;
using Aethermeter.UI;

namespace Aethermeter.Windows;

/// <summary>Everything about one player in one segment: abilities, damage taken, deaths and a graph.</summary>
public sealed class BreakdownWindow : Window
{
    private readonly Plugin plugin;
    private SegmentRef segment;
    private uint entityId;
    private int tab;
    private int openDeath = -1;
    private string title = "Breakdown";

    public BreakdownWindow(Plugin plugin) : base("Aethermeter Breakdown")
    {
        this.plugin = plugin;
        Size = new Vector2(640, 460);
        SizeMin = new Vector2(420, 280);
    }

    private Configuration Config => plugin.Configuration;

    public void Show(SegmentRef segment, uint entityId, MeterMode mode)
    {
        this.segment = segment;
        this.entityId = entityId;
        tab = mode switch
        {
            MeterMode.Hps or MeterMode.Healing or MeterMode.Overhealing => 1,
            MeterMode.DamageTaken => 2,
            MeterMode.Deaths => 3,
            _ => 0,
        };
        openDeath = -1;
        IsOpen = true;
    }

    protected override string Title() => title;

    protected override void DrawContents()
    {
        lock (plugin.Encounters.Sync)
        {
            var encounter = plugin.Encounters.Resolve(segment);
            if (encounter is null || !encounter.Combatants.TryGetValue(entityId, out var c))
            {
                title = "Breakdown";
                ImGui.TextColored(Theme.TextMuted, "This player isn't in the selected segment.");
                return;
            }

            var name = plugin.Names.For(c, encounter);
            title = $"{name} — {encounter.Title}";
            var seconds = encounter.Duration(plugin.Now);

            DrawSummary(c, encounter, name, seconds);
            Theme.Tabs("##bd", ref tab,
                (FontAwesomeIcon.Fire, "Damage"),
                (FontAwesomeIcon.Heart, "Healing"),
                (FontAwesomeIcon.ShieldAlt, "Taken"),
                (FontAwesomeIcon.SkullCrossbones, $"Deaths ({c.Deaths.Count})"),
                (FontAwesomeIcon.ChartArea, "Graph"));

            using var body = ImRaii.Child("##bd-body", Vector2.Zero);
            switch (tab)
            {
                case 0: DrawAbilities(c.DamageAbilities.Values, c.Damage, seconds, healing: false); break;
                case 1: DrawAbilities(c.HealAbilities.Values, c.Healing + c.Overheal, seconds, healing: true); break;
                case 2: DrawTaken(c, encounter); break;
                case 3: DrawDeaths(c, encounter); break;
                case 4: DrawGraph(c, encounter); break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Summary
    // ------------------------------------------------------------------

    private void DrawSummary(Combatant c, Encounter encounter, string name, double seconds)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        const float h = 58f;
        var color = JobColors.For(c, Config);

        dl.AddRectFilled(p, p + new Vector2(w, h), Theme.U32(Theme.Surface, 0.8f), Theme.Radius);
        dl.AddRectFilledMultiColor(p, p + new Vector2(w * 0.6f, h),
            Theme.U32(color, 0.22f), Theme.U32(color, 0f), Theme.U32(color, 0f), Theme.U32(color, 0.22f));
        dl.AddRectFilled(p, p + new Vector2(3, h), Theme.U32(color), 1.5f);

        var job = GameData.Profile(c);
        var x = p.X + 12;
        if (job.Icon != 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.Icon)).GetWrapOrEmpty();
            dl.AddImage(wrap.Handle, new Vector2(x, p.Y + 9), new Vector2(x + 40, p.Y + 49));
            x += 50;
        }

        var header = plugin.Fonts.Get(Config.HeaderFont);
        var nameFont = plugin.Fonts.Get(Config.NameFont);
        dl.AddText(nameFont, 20f, new Vector2(x, p.Y + 8), Theme.U32(Theme.Text), name);
        var sub = $"{job.Name}   ·   {encounter.Title}   ·   {Format.Duration(seconds)}";
        dl.AddText(header, 15f, new Vector2(x, p.Y + 33), Theme.U32(Theme.TextMuted), sub);

        // Stat blocks, right to left.
        var stats = new List<(string Label, string Value, Vector4 Tint)>
        {
            ("DPS", Format.Number(c.Damage / Math.Max(seconds, 1), Config.NumberFormat), Theme.AetherBright),
            ("HPS", Format.Number(c.Healing / Math.Max(seconds, 1), Config.NumberFormat), JobColors.Healer),
            ("CRIT", Format.Percent(c.CritRate), Theme.Text),
            ("DH", Format.Percent(c.DirectHitRate), Theme.Text),
            ("DEATHS", c.Deaths.Count.ToString(), c.Deaths.Count > 0 ? Theme.Ember : Theme.TextMuted),
        };

        var num = plugin.Fonts.Get(Config.NumberFont);
        var right = p.X + w - 12;
        for (var i = stats.Count - 1; i >= 0; i--)
        {
            var (label, value, tint) = stats[i];
            var vw = Math.Max(Theme.Measure(num, 22f, value), Theme.Measure(header, 12f, label));
            if (right - vw < x + 220) break;
            dl.AddText(num, 22f, new Vector2(right - Theme.Measure(num, 22f, value), p.Y + 8), Theme.U32(tint), value);
            dl.AddText(header, 12f, new Vector2(right - Theme.Measure(header, 12f, label), p.Y + 35), Theme.U32(Theme.TextFaint), label);
            right -= vw + 18;
        }

        ImGui.Dummy(new Vector2(w, h + 6));
    }

    // ------------------------------------------------------------------
    // Ability tables
    // ------------------------------------------------------------------

    private void DrawAbilities(IEnumerable<AbilityStats> source, long total, double seconds, bool healing)
    {
        var list = source.Where(a => a.Total > 0 || a.Misses > 0).OrderByDescending(a => a.Total).ToList();
        if (list.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, healing ? "No healing in this segment." : "No damage in this segment.");
            return;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##abilities", healing ? 8 : 9, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(healing ? "Healing" : "Damage");
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn(healing ? "HPS" : "DPS");
        ImGui.TableSetupColumn("Hits");
        ImGui.TableSetupColumn("Crit");
        if (healing)
            ImGui.TableSetupColumn("Overheal");
        else
        {
            ImGui.TableSetupColumn("DH");
            ImGui.TableSetupColumn("Avg");
        }

        ImGui.TableSetupColumn("Max");
        ImGui.TableHeadersRow();

        var iconSize = ImGui.GetTextLineHeight() + 2;
        foreach (var a in list)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            AbilityLabel(a, iconSize);

            var effective = healing ? a.Total - a.Overheal : a.Total;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format.Number(effective, Config.NumberFormat));

            ImGui.TableNextColumn();
            ShareBar(total > 0 ? (float)a.Total / total : 0f, healing ? JobColors.Healer : Theme.Aether);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format.Number(effective / Math.Max(seconds, 1), Config.NumberFormat));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(a.Misses > 0 ? $"{a.Hits} ({a.Misses} miss)" : a.Hits.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(a.Hits > 0 ? Format.Percent((double)a.Crits / a.Hits) : "-");

            if (healing)
            {
                ImGui.TableNextColumn();
                if (a.IsShield)
                    ImGui.TextColored(Theme.TextFaint, "absorbed");
                else
                    ImGui.TextColored(a.Overheal > 0 ? Theme.TextMuted : Theme.TextFaint, a.Total > 0 ? Format.Percent((double)a.Overheal / a.Total) : "-");
            }
            else
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(a.Hits > 0 ? Format.Percent((double)a.DirectHits / a.Hits) : "-");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(a.Hits > 0 ? Format.Number((double)a.Total / a.Hits, Config.NumberFormat) : "-");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(a.Hits > 0 ? Format.Number(a.Max, Config.NumberFormat) : "-");
        }

        ImGui.EndTable();
    }

    private static void AbilityLabel(AbilityStats a, float iconSize)
    {
        var icon = GameData.AbilityIcon(a.ActionId, a.StatusId);
        if (icon != 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
            // Status icons are taller than wide; keep their aspect.
            var size = a.IsTick ? new Vector2(iconSize * 0.75f, iconSize) : new Vector2(iconSize, iconSize);
            ImGui.Image(wrap.Handle, size);
            ImGui.SameLine(0, 6);
        }
        else
        {
            ImGui.Dummy(new Vector2(iconSize, iconSize));
            ImGui.SameLine(0, 6);
        }

        ImGui.TextUnformatted(GameData.AbilityName(a.ActionId, a.StatusId, a.IsShield));
        if (a.PetName is not null)
        {
            ImGui.SameLine(0, 4);
            ImGui.TextColored(Theme.TextMuted, $"({a.PetName})");
        }
    }

    private static void ShareBar(float share, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var h = ImGui.GetTextLineHeight();
        dl.AddRectFilled(p + new Vector2(0, h * 0.25f), p + new Vector2(w, h * 0.75f), Theme.U32(Theme.Surface), 2f);
        dl.AddRectFilled(p + new Vector2(0, h * 0.25f), p + new Vector2(w * Math.Clamp(share, 0f, 1f), h * 0.75f), Theme.U32(color, 0.8f), 2f);
        ImGui.Dummy(new Vector2(w, h));
        if (ImGui.IsItemHovered()) Theme.Tooltip(Format.Percent(share));
    }

    private void DrawTaken(Combatant c, Encounter encounter)
    {
        var list = c.TakenAbilities.Values.Where(a => a.Total > 0).OrderByDescending(a => a.Total).ToList();
        if (list.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, "No damage taken in this segment.");
            return;
        }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##taken", 6, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("From");
        ImGui.TableSetupColumn("Damage");
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Hits");
        ImGui.TableSetupColumn("Max");
        ImGui.TableHeadersRow();

        var iconSize = ImGui.GetTextLineHeight() + 2;
        foreach (var a in list)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            AbilityLabel(a, iconSize);
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextMuted, plugin.Names.Mask(a.SourceName ?? "?", encounter));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format.Number(a.Total, Config.NumberFormat));
            ImGui.TableNextColumn();
            ShareBar(c.DamageTaken > 0 ? (float)a.Total / c.DamageTaken : 0f, Theme.Ember);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(a.Hits.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format.Number(a.Max, Config.NumberFormat));
        }

        ImGui.EndTable();
    }

    // ------------------------------------------------------------------
    // Deaths
    // ------------------------------------------------------------------

    private void DrawDeaths(Combatant c, Encounter encounter)
    {
        if (c.Deaths.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, "No deaths in this segment. Nice.");
            return;
        }

        for (var i = 0; i < c.Deaths.Count; i++)
        {
            var death = c.Deaths[i];
            var at = death.Time - encounter.Start;
            var blow = death.KillingBlow;
            var cause = blow is { } b
                ? $"{GameData.AbilityName(b.ActionId, b.StatusId)} from {plugin.Names.Mask(b.Source, encounter)} ({Format.Number(b.Amount, Config.NumberFormat)})"
                : "cause unknown";

            using var id = ImRaii.PushId(i);
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Ember))
            {
                if (ImGui.Selectable($"Death {i + 1}", openDeath == i, ImGuiSelectableFlags.None, new Vector2(80, 0)))
                    openDeath = openDeath == i ? -1 : i;
            }

            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, encounter.IsOverall ? cause : $"{Format.Duration(at)}  ·  {cause}");

            if (openDeath == i) DrawRecap(death, encounter);
        }
    }

    private void DrawRecap(DeathRecord death, Encounter encounter)
    {
        if (death.Lines.Length == 0)
        {
            ImGui.TextColored(Theme.TextFaint, "Nothing was recorded in the seconds before this death.");
            return;
        }

        using var indent = ImRaii.PushIndent(14f);
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerH;
        if (!ImGui.BeginTable("##recap", 5, flags)) return;

        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("From");
        ImGui.TableSetupColumn("Amount");
        ImGui.TableHeadersRow();

        foreach (var line in death.Lines)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextMuted, Format.Offset(line.Time - death.Time));

            ImGui.TableNextColumn();
            var share = line.MaxHp > 0 ? (float)line.HpBefore / line.MaxHp : 0f;
            ShareBar(share, share > 0.5f ? JobColors.Healer : share > 0.25f ? Theme.Gold : Theme.Ember);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GameData.AbilityName(line.ActionId, line.StatusId, line.Shield));

            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextMuted, plugin.Names.Mask(line.Source, encounter));

            ImGui.TableNextColumn();
            var heal = line.Kind == EventKind.Heal;
            var text = (heal ? "+" : "-") + Format.Number(line.Amount, Config.NumberFormat) + (line.Crit ? "!" : string.Empty);
            if (line.Shield) text += " absorbed";
            ImGui.TextColored(heal ? JobColors.Healer : Theme.Ember, text);
        }

        ImGui.EndTable();
    }

    // ------------------------------------------------------------------
    // Graph
    // ------------------------------------------------------------------

    private void DrawGraph(Combatant c, Encounter encounter)
    {
        var dmg = Smooth(c.DamageSeries, 5);
        var heal = Smooth(c.HealSeries, 5);
        var length = Math.Max(dmg.Length, heal.Length);
        if (length < 2)
        {
            ImGui.TextColored(Theme.TextMuted, "Not enough data for a graph yet.");
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var size = new Vector2(avail.X, Math.Max(avail.Y - 24, 120));
        const float left = 46f;
        const float bottom = 18f;

        var plotMin = new Vector2(p.X + left, p.Y + 6);
        var plotMax = new Vector2(p.X + size.X - 6, p.Y + size.Y - bottom);
        dl.AddRectFilled(p, p + size, Theme.U32(Theme.Surface, 0.45f), Theme.Radius);

        double peak = Math.Max(dmg.DefaultIfEmpty(0).Max(), heal.DefaultIfEmpty(0).Max());
        if (peak <= 0) peak = 1;
        peak = NiceCeiling(peak);

        // Horizontal guides with their values.
        for (var i = 0; i <= 4; i++)
        {
            var y = plotMax.Y - (plotMax.Y - plotMin.Y) * i / 4f;
            dl.AddLine(new Vector2(plotMin.X, y), new Vector2(plotMax.X, y), Theme.U32(Theme.Line, i == 0 ? 1f : 0.5f), 1f);
            var label = Format.Number(peak * i / 4, NumberFormat.Short);
            var lw = ImGui.CalcTextSize(label).X;
            dl.AddText(new Vector2(plotMin.X - 6 - lw, y - ImGui.GetTextLineHeight() * 0.5f), Theme.U32(Theme.TextFaint), label);
        }

        // Time ticks every 30 s, or every minute on long fights.
        var step = length > 360 ? 60 : 30;
        for (var t = 0; t < length; t += step)
        {
            var x = plotMin.X + (plotMax.X - plotMin.X) * t / (length - 1f);
            dl.AddLine(new Vector2(x, plotMax.Y), new Vector2(x, plotMax.Y + 3), Theme.U32(Theme.Line), 1f);
            dl.AddText(new Vector2(x + 2, plotMax.Y + 2), Theme.U32(Theme.TextFaint), Format.Duration(t));
        }

        if (!encounter.IsOverall)
        {
            foreach (var death in c.Deaths)
            {
                var t = (float)(death.Time - encounter.Start);
                var x = plotMin.X + (plotMax.X - plotMin.X) * t / (length - 1f);
                if (x < plotMin.X || x > plotMax.X) continue;
                dl.AddLine(new Vector2(x, plotMin.Y), new Vector2(x, plotMax.Y), Theme.U32(Theme.Ember, 0.7f), 1.5f);
            }
        }

        Area(dl, heal, length, plotMin, plotMax, (float)peak, JobColors.Healer);
        Area(dl, dmg, length, plotMin, plotMax, (float)peak, JobColors.For(c, Config));

        // Crosshair readout under the mouse.
        ImGui.SetCursorScreenPos(plotMin);
        ImGui.InvisibleButton("##graph", Vector2.Max(plotMax - plotMin, Vector2.One));
        if (ImGui.IsItemHovered())
        {
            var mx = ImGui.GetMousePos().X;
            var t = (int)Math.Round((mx - plotMin.X) / (plotMax.X - plotMin.X) * (length - 1));
            t = Math.Clamp(t, 0, length - 1);
            var x = plotMin.X + (plotMax.X - plotMin.X) * t / (length - 1f);
            dl.AddLine(new Vector2(x, plotMin.Y), new Vector2(x, plotMax.Y), Theme.U32(Theme.Text, 0.4f), 1f);
            Theme.Tooltip($"{Format.Duration(t)}\nDPS  {Format.Number(t < dmg.Length ? dmg[t] : 0, Config.NumberFormat)}\nHPS  {Format.Number(t < heal.Length ? heal[t] : 0, Config.NumberFormat)}");
        }

        ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + size.Y + 4));
        Legend(JobColors.For(c, Config), "Damage per second (5 s average)");
        ImGui.SameLine(0, 18);
        Legend(JobColors.Healer, "Healing per second");
    }

    private static void Legend(Vector4 color, string label)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight();
        dl.AddRectFilled(p + new Vector2(0, h * 0.3f), p + new Vector2(10, h * 0.7f), Theme.U32(color), 2f);
        ImGui.Dummy(new Vector2(14, h));
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Theme.TextMuted, label);
    }

    private static void Area(ImDrawListPtr dl, float[] series, int length, Vector2 min, Vector2 max, float peak, Vector4 color)
    {
        if (series.Length < 2 || series.All(v => v <= 0)) return;

        Vector2 Point(int i) => new(
            min.X + (max.X - min.X) * i / (length - 1f),
            max.Y - (max.Y - min.Y) * Math.Clamp(series[i] / peak, 0f, 1f));

        // Fill as thin vertical slabs (ImGui has no concave polygon fill), then stroke the line.
        for (var i = 1; i < series.Length; i++)
        {
            var a = Point(i - 1);
            var b = Point(i);
            dl.AddQuadFilled(a, b, new Vector2(b.X, max.Y), new Vector2(a.X, max.Y), Theme.U32(color, 0.16f));
        }

        for (var i = 1; i < series.Length; i++)
            dl.AddLine(Point(i - 1), Point(i), Theme.U32(color, 0.95f), 1.8f);
    }

    private static float[] Smooth(List<float> series, int window)
    {
        var result = new float[series.Count];
        double sum = 0;
        for (var i = 0; i < series.Count; i++)
        {
            sum += series[i];
            if (i >= window) sum -= series[i - window];
            result[i] = (float)(sum / Math.Min(i + 1, window));
        }

        return result;
    }

    private static double NiceCeiling(double value)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * magnitude >= value) return m * magnitude;
        return 10 * magnitude;
    }
}
