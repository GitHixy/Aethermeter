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

/// <summary>
/// Party cooldowns: who has their raid buffs, mitigation, healing cooldowns and invulnerabilities
/// ready, as a grid of icons or as a timeline of the pull. Drawn in the same frame as the meters.
/// </summary>
public sealed class CooldownWindow
{
    private readonly Plugin plugin;
    private bool moveRequested;
    private float watermarkHalf;

    public CooldownWindow(Plugin plugin) => this.plugin = plugin;

    private Configuration Config => plugin.Configuration;
    private CooldownWindowConfig Settings => plugin.Configuration.Cooldowns;

    public void MoveTo(Vector2 position)
    {
        Settings.Position = position;
        moveRequested = true;
    }

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
        ImGui.SetNextWindowSizeConstraints(new Vector2(200, 80), new Vector2(2400, 2000));
        ImGui.SetNextWindowBgAlpha(Config.BackgroundOpacity);

        using var theme = Theme.Push();
        using var chrome = Chrome.Push(Config, new Vector2(200, 80));

        if (ImGui.Begin("##aethermeter-cooldowns", flags))
        {
            try
            {
                Track();
                DrawContents();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error drawing the cooldown window");
            }
        }

        ImGui.End();
    }

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

    private bool Include(CooldownKind kind) => kind switch
    {
        CooldownKind.RaidBuff => Settings.ShowRaidBuffs,
        CooldownKind.Mitigation => Settings.ShowMitigation,
        CooldownKind.Healing => Settings.ShowHealing,
        CooldownKind.Invuln => Settings.ShowInvulns,
        _ => Settings.ShowUtility,
    };

    private void DrawContents()
    {
        var dl = ImGui.GetWindowDrawList();
        var winMin = ImGui.GetWindowPos();
        var winMax = winMin + ImGui.GetWindowSize();
        var now = plugin.Now;

        var headerH = DrawHeader(dl, winMin, winMax.X);
        var bodyMin = new Vector2(winMin.X, winMin.Y + headerH);
        Chrome.Backdrop(dl, Config, bodyMin, winMax);
        watermarkHalf = Chrome.Watermark(dl, plugin.Fonts.Get(FontId.BarlowCondensed), Config, bodyMin, winMax);

        ImGui.SetCursorScreenPos(bodyMin);
        using (ImRaii.Child("##cds", winMax - bodyMin, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground))
        {
            var roster = plugin.CooldownRoster();
            var rows = roster
                .Select(a => (Who: a, Defs: CooldownCatalog.For(a.JobId, Include).ToList()))
                .Where(r => r.Defs.Count > 0)
                .ToList();

            if (rows.Count == 0)
                DrawEmpty(ImGui.GetWindowDrawList(), bodyMin, winMax, roster.Count == 0 ? "No party members" : "Nothing to show with these filters");
            else if (Settings.View == CooldownView.Timeline)
                DrawTimeline(rows, roster, bodyMin, winMax, now);
            else
                DrawGrid(rows, roster, bodyMin, winMax, now);
        }

        DrawPopups();
    }

    // ------------------------------------------------------------------
    // Header
    // ------------------------------------------------------------------

    private float DrawHeader(ImDrawListPtr dl, Vector2 min, float right)
    {
        var font = plugin.Fonts.Get(Config.HeaderFont);
        var size = Config.HeaderFontSize;
        var h = MathF.Round(size + 14f);
        Chrome.Header(dl, Config, min, new Vector2(right, min.Y + h));

        var x = min.X + 9;
        if (Config.ShowHeaderLogo)
        {
            var logo = h - 12f;
            Theme.Logo(dl, new Vector2(min.X + 7, min.Y + 6), logo, accent: Config.WindowAccent);
            x = min.X + 13 + logo;
        }

        var btn = h - 6f;
        var bx = right - 4 - btn;
        ImGui.SetCursorScreenPos(new Vector2(bx, min.Y + 3));
        if (Theme.IconButton("##cd-menu", FontAwesomeIcon.Bars, "Menu", null, btn)) ImGui.OpenPopup("##cd-menu");
        bx -= btn;
        ImGui.SetCursorScreenPos(new Vector2(bx, min.Y + 3));
        var timeline = Settings.View == CooldownView.Timeline;
        if (Theme.IconButton("##cd-view", timeline ? FontAwesomeIcon.ThLarge : FontAwesomeIcon.Stream,
                timeline ? "Show as a grid" : "Show as a timeline", null, btn))
        {
            Settings.View = timeline ? CooldownView.Grid : CooldownView.Timeline;
            plugin.SaveSoon();
        }

        var title = Theme.Fit(font, size, timeline ? "Cooldown timeline" : "Party cooldowns", bx - 8 - x);
        Theme.Text2(dl, font, size, new Vector2(x, min.Y + (h - size) * 0.5f), Theme.U32(Config.TextColor), title, false);
        return h;
    }

    // ------------------------------------------------------------------
    // Rows
    // ------------------------------------------------------------------

    private float RowHeight => MathF.Round(Math.Clamp(Settings.IconSize, 14f, 48f)) + 4f;

    /// <summary>The job icon and (room allowing) the name; returns where the row's content starts.</summary>
    private float DrawMember(ImDrawListPtr dl, Actor who, IReadOnlyList<Actor> roster, Vector2 min, float rowH, float width)
    {
        var icon = rowH - 4f;
        var x = min.X + 6;
        var job = GameData.JobInfo(who.JobId);
        if (job.Icon != 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.Icon)).GetWrapOrEmpty();
            dl.AddImage(wrap.Handle, new Vector2(x, min.Y + 2), new Vector2(x + icon, min.Y + 2 + icon));
        }

        x += icon + 5;
        if (!Settings.ShowNames || width < 240) return x;

        var font = plugin.Fonts.Get(Config.NameFont);
        var size = Math.Min(Config.NameFontSize, rowH - 4);
        var nameW = Math.Min(110f, width * 0.3f);
        var name = Theme.Fit(font, size, plugin.Names.For(who, roster), nameW - 6);
        var color = who.IsLocalPlayer && Config.HighlightSelf ? Chrome.AccentBright(Config) : Config.TextColor;
        Theme.Text2(dl, font, size, new Vector2(x, min.Y + (rowH - size) * 0.5f), Theme.U32(color), name, Config.TextShadow);
        return x + nameW;
    }

    private void DrawGrid(List<(Actor Who, List<CooldownDef> Defs)> rows, IReadOnlyList<Actor> roster, Vector2 min, Vector2 max, double now)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowH = RowHeight;
        var icon = rowH - 4f;
        var width = max.X - min.X;
        var y = min.Y + 4;
        var numFont = plugin.Fonts.Get(Config.NumberFont);
        var pullStart = PullStart();

        foreach (var (who, defs) in rows)
        {
            if (y + rowH > max.Y + 1) break;
            var x = DrawMember(dl, who, roster, new Vector2(min.X, y), rowH, width);

            foreach (var def in defs)
            {
                if (x + icon > max.X - 4) break;
                var p = new Vector2(x, y + 2);
                var q = p + new Vector2(icon, icon);
                var remaining = plugin.Cooldowns.Remaining(who.EntityId, def, now);
                DrawIcon(dl, def, p, q, remaining, plugin.Cooldowns.ReadyAt(who.EntityId, def), now, numFont);

                ImGui.SetCursorScreenPos(p);
                ImGui.InvisibleButton($"##cd{who.EntityId}-{def.ActionId}", new Vector2(icon, icon));
                if (ImGui.IsItemHovered())
                {
                    var uses = pullStart is { } s ? plugin.Cooldowns.CountSince(who.EntityId, def, s) : 0;
                    var state = remaining > 0 ? $"Ready in {Format.Duration(remaining)}" : "Ready";
                    Theme.Tooltip($"{def.Name}\n{CooldownCatalog.KindLabel(def.Kind)} · {def.Recast:0} s recast\n{state}"
                                  + (pullStart is null ? string.Empty : $"\nUsed {uses}× this pull"));
                }

                x += icon + 3;
            }

            y += rowH;
        }
    }

    private void DrawIcon(ImDrawListPtr dl, CooldownDef def, Vector2 p, Vector2 q, double remaining, double? readyAt, double now, ImFontPtr numFont)
    {
        var size = q.X - p.X;
        var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(def.Icon)).GetWrapOrEmpty();
        var rounding = Math.Min(Config.Rounding, size * 0.25f);

        if (remaining > 0)
        {
            dl.AddImage(wrap.Handle, p, q, Vector2.Zero, Vector2.One, Theme.U32(new Vector4(0.55f, 0.55f, 0.55f, 1f)));
            // The dark part shrinks from the top as the recast runs out.
            var frac = (float)Math.Clamp(remaining / def.Recast, 0, 1);
            dl.AddRectFilled(p, new Vector2(q.X, p.Y + size * frac), Theme.U32(new Vector4(0, 0, 0, 0.55f)));

            var text = remaining >= 60 ? $"{(int)Math.Ceiling(remaining / 60)}m" : $"{(int)Math.Ceiling(remaining)}";
            var ts = size * 0.55f;
            var tw = Theme.Measure(numFont, ts, text);
            Theme.Text2(dl, numFont, ts, new Vector2(p.X + (size - tw) * 0.5f, p.Y + (size - ts) * 0.5f), Theme.U32(Config.TextColor), text, true);
            dl.AddRect(p, q, Theme.U32(Theme.Line), rounding, ImDrawFlags.None, 1f);
            return;
        }

        dl.AddImage(wrap.Handle, p, q);
        // Just came back: a short accent flash, so it's noticed.
        var since = readyAt is { } r ? now - r : double.MaxValue;
        if (since < 2.0 && !plugin.ReducedMotion)
        {
            var a = (float)(1 - since / 2.0);
            dl.AddRect(p - Vector2.One, q + Vector2.One, Theme.U32(Chrome.AccentBright(Config), a), rounding, ImDrawFlags.None, 2f);
        }
        else
            dl.AddRect(p, q, Theme.U32(Config.WindowAccent, 0.35f), rounding, ImDrawFlags.None, 1f);
    }

    // ------------------------------------------------------------------
    // Timeline
    // ------------------------------------------------------------------

    /// <summary>The pull the timeline shows: the running one, or the last one. Null before any combat.</summary>
    private (double Start, double End, bool Live)? Pull(double now)
    {
        lock (plugin.Encounters.Sync)
        {
            var e = plugin.Encounters.Resolve(SegmentRef.Current);
            if (e is null) return null;
            return (e.Start, e.End ?? now, e.End is null);
        }
    }

    private double? PullStart() => Pull(plugin.Now)?.Start;

    private void DrawTimeline(List<(Actor Who, List<CooldownDef> Defs)> rows, IReadOnlyList<Actor> roster, Vector2 min, Vector2 max, double now)
    {
        var dl = ImGui.GetWindowDrawList();
        if (Pull(now) is not { } pull)
        {
            DrawEmpty(dl, min, max, "Waiting for combat");
            return;
        }

        var rowH = RowHeight;
        var width = max.X - min.X;
        var axisH = 14f;
        var smallFont = plugin.Fonts.Get(Config.NumberFont);
        var axisSize = Math.Min(Config.NumberFontSize * 0.7f, 13f);

        // The name column, measured once so every row's track lines up.
        var left = DrawMemberColumnWidth(rowH, width) + min.X;
        var right = max.X - 8;
        if (right - left < 40) return;
        var span = Math.Max(pull.End - pull.Start, 30);
        float X(double t) => left + (float)((t - pull.Start) / span) * (right - left);

        // Axis: a mark every 30 s (60 s past five minutes), labelled when there's room.
        var step = span > 300 ? 60 : 30;
        for (var s = 0; s <= span; s += step)
        {
            var x = X(pull.Start + s);
            dl.AddLine(new Vector2(x, min.Y + axisH), new Vector2(x, max.Y), Theme.U32(Theme.Line, 0.6f), 1f);
            dl.AddText(smallFont, axisSize, new Vector2(x + 2, min.Y + 1), Theme.U32(Theme.TextMuted), Format.Duration(s));
        }

        if (pull.Live)
            dl.AddLine(new Vector2(X(now), min.Y + axisH), new Vector2(X(now), max.Y), Theme.U32(Theme.Ember, 0.7f), 1.5f);

        var uses = plugin.Cooldowns.Between(pull.Start, pull.End);
        var icon = rowH - 6f;
        var y = min.Y + axisH + 2;

        foreach (var (who, defs) in rows)
        {
            if (y + rowH > max.Y + 1) break;
            DrawMember(dl, who, roster, new Vector2(min.X, y), rowH, width);
            dl.AddLine(new Vector2(left, y + rowH - 0.5f), new Vector2(right, y + rowH - 0.5f), Theme.U32(Theme.LineSoft), 1f);

            foreach (var u in uses)
            {
                if (u.Who.EntityId != who.EntityId || !defs.Contains(u.Def)) continue;

                var x = X(u.Time);
                var mid = y + rowH * 0.5f;
                // The recast, as a thin line to when it's back.
                var back = Math.Min(X(u.Time + u.Def.Recast), right);
                dl.AddLine(new Vector2(x, mid), new Vector2(back, mid), Theme.U32(Config.WindowAccent, 0.35f), 2f);
                if (back < right) dl.AddLine(new Vector2(back, mid - 3), new Vector2(back, mid + 3), Theme.U32(Config.WindowAccent, 0.6f), 1.5f);

                var p = new Vector2(x - icon * 0.5f, y + 3);
                var q = p + new Vector2(icon, icon);
                var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(u.Def.Icon)).GetWrapOrEmpty();
                dl.AddImage(wrap.Handle, p, q);
                dl.AddRect(p, q, Theme.U32(Theme.Ink), 0f, ImDrawFlags.None, 1f);

                if (ImGui.IsMouseHoveringRect(p, q) && ImGui.IsWindowHovered())
                    Theme.Tooltip($"{u.Def.Name}\n{plugin.Names.For(who, roster)} at {Format.Duration(u.Time - pull.Start)}\n{CooldownCatalog.KindLabel(u.Def.Kind)} · {u.Def.Recast:0} s recast");
            }

            y += rowH;
        }
    }

    private float DrawMemberColumnWidth(float rowH, float width)
        => 6 + (rowH - 4f) + 5 + (Settings.ShowNames && width >= 240 ? Math.Min(110f, width * 0.3f) : 0f);

    private void DrawEmpty(ImDrawListPtr dl, Vector2 min, Vector2 max, string text)
    {
        var font = plugin.Fonts.Get(Config.HeaderFont);
        var size = Config.HeaderFontSize * 0.9f;
        var w = Theme.Measure(font, size, text);
        var center = (min + max) * 0.5f;
        var y = watermarkHalf > 0 ? Math.Min(center.Y + watermarkHalf + 8f, max.Y - size - 4f) : center.Y - size * 0.5f;
        dl.AddText(font, size, new Vector2(center.X - w * 0.5f, y), Theme.U32(Theme.TextFaint), text);
    }

    // ------------------------------------------------------------------
    // Menu
    // ------------------------------------------------------------------

    private void DrawPopups()
    {
        using var popup = ImRaii.Popup("##cd-menu");
        if (!popup) return;

        ImGui.TextColored(Theme.TextFaint, "VIEW");
        if (ImGui.MenuItem("Grid", string.Empty, Settings.View == CooldownView.Grid)) Set(() => Settings.View = CooldownView.Grid);
        if (ImGui.MenuItem("Timeline", string.Empty, Settings.View == CooldownView.Timeline)) Set(() => Settings.View = CooldownView.Timeline);

        ImGui.Separator();
        ImGui.TextColored(Theme.TextFaint, "SHOW");
        Toggle("Raid buffs", Settings.ShowRaidBuffs, v => Settings.ShowRaidBuffs = v);
        Toggle("Party mitigation", Settings.ShowMitigation, v => Settings.ShowMitigation = v);
        Toggle("Healing cooldowns", Settings.ShowHealing, v => Settings.ShowHealing = v);
        Toggle("Tank invulnerabilities", Settings.ShowInvulns, v => Settings.ShowInvulns = v);
        Toggle("Utility (Swiftcast, Rescue)", Settings.ShowUtility, v => Settings.ShowUtility = v);
        Toggle("Names", Settings.ShowNames, v => Settings.ShowNames = v);

        ImGui.Separator();
        Toggle("Lock window", Settings.Locked, v => Settings.Locked = v);
        Toggle("Click-through when locked", Settings.ClickThrough, v => Settings.ClickThrough = v);
        if (ImGui.MenuItem("Settings")) plugin.ConfigWindow.IsOpen = true;
        if (ImGui.MenuItem("Close")) Set(() => Settings.Open = false);
    }

    private void Toggle(string label, bool value, Action<bool> set)
    {
        if (ImGui.MenuItem(label, string.Empty, ref value)) Set(() => set(value));
    }

    private void Set(Action change)
    {
        change();
        plugin.SaveSoon();
    }
}
