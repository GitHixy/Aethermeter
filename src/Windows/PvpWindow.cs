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

/// <summary>Which column the PvP table is ordered by.</summary>
public enum PvpSort
{
    Kills,
    Deaths,
    Assists,
    Damage,
    Healing,
    Taken,
}

/// <summary>
/// PvP stats for the running (or last) match: kills, deaths and assists for everyone on our side,
/// with their damage to players, healing and damage taken, the team's score and a kill feed.
/// Drawn in the same frame as the meters, in the same chrome.
/// </summary>
public sealed class PvpWindow
{
    private const int FeedLines = 6;

    private readonly Plugin plugin;
    private bool moveRequested;
    private float watermarkHalf;
    /// <summary>A past match picked from the header; null follows the latest.</summary>
    private int? fixedMatch;

    public PvpWindow(Plugin plugin) => this.plugin = plugin;

    private Configuration Config => plugin.Configuration;
    private PvpWindowConfig Settings => plugin.Configuration.Pvp;

    public PvpSort Sort { get; set; } = PvpSort.Kills;

    public void MoveTo(Vector2 position)
    {
        Settings.Position = position;
        moveRequested = true;
    }

    /// <summary>On screen when open and, with "only in PvP", while there's PvP to show.</summary>
    private bool Visible()
    {
        if (!Settings.Open) return false;
        if (!Settings.OnlyInPvp || plugin.ConfigWindow.IsOpen) return true;
        return Plugin.ClientState.IsPvP || plugin.Test is { Running: true, Pvp: true };
    }

    public void Draw()
    {
        if (!Visible()) return;

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;
        if (Settings.Locked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        if (Settings.Locked && Settings.ClickThrough) flags |= ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(Settings.Position, moveRequested ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        moveRequested = false;
        ImGui.SetNextWindowSize(Settings.Size, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(220, 90), new Vector2(2400, 2000));
        ImGui.SetNextWindowBgAlpha(Config.BackgroundOpacity);

        using var theme = Theme.Push();
        using var chrome = Chrome.Push(Config, new Vector2(220, 90));

        if (ImGui.Begin("##aethermeter-pvp", flags))
        {
            try
            {
                Track();
                DrawContents();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error drawing the PvP window");
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

    /// <summary>The match shown: the one picked, else the running one, else the last one played.</summary>
    private Encounter? Match()
    {
        var encounters = plugin.Encounters;
        if (fixedMatch is { } id)
        {
            var picked = encounters.History.FirstOrDefault(e => e.Id == id);
            if (picked is not null) return picked;
            fixedMatch = null;
        }

        return encounters.Active is { IsPvp: true } active ? active : encounters.History.FirstOrDefault(e => e.IsPvp);
    }

    /// <summary>How the breakdown window finds this match again.</summary>
    private SegmentRef SegmentOf(Encounter e)
        => ReferenceEquals(e, plugin.Encounters.Resolve(SegmentRef.Current)) ? SegmentRef.Current : new SegmentRef(SegmentKind.Fixed, e.Id);

    private void DrawContents()
    {
        var dl = ImGui.GetWindowDrawList();
        var winMin = ImGui.GetWindowPos();
        var winMax = winMin + ImGui.GetWindowSize();
        var now = plugin.Now;

        lock (plugin.Encounters.Sync)
        {
            var match = Match();
            var headerH = DrawHeader(dl, winMin, winMax.X, match, now);
            var bodyMin = new Vector2(winMin.X, winMin.Y + headerH);
            Chrome.Backdrop(dl, Config, bodyMin, winMax);
            watermarkHalf = Chrome.Watermark(dl, plugin.Fonts.Get(FontId.BarlowCondensed), Config, bodyMin, winMax);

            ImGui.SetCursorScreenPos(bodyMin);
            using (ImRaii.Child("##pvp", winMax - bodyMin, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground))
            {
                if (match is null)
                    DrawEmpty(ImGui.GetWindowDrawList(), bodyMin, winMax, "Waiting for a PvP match");
                else
                    DrawBody(match, bodyMin, winMax, now);
            }

            DrawPopups();
        }
    }

    // ------------------------------------------------------------------
    // Header
    // ------------------------------------------------------------------

    private float DrawHeader(ImDrawListPtr dl, Vector2 min, float right, Encounter? match, double now)
    {
        var font = plugin.Fonts.Get(Config.HeaderFont);
        var size = Config.HeaderFontSize;
        var h = MathF.Round(size + 14f);
        var textY = min.Y + (h - size) * 0.5f;
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
        if (Theme.IconButton("##pvp-menu", FontAwesomeIcon.Bars, "Menu", null, btn)) ImGui.OpenPopup("##pvp-menu");

        var timer = match is null ? "--:--" : Format.Duration(match.Duration(now));
        var timerW = Theme.Measure(font, size, timer);
        var timerX = bx - 8 - timerW;
        var live = match is { Active: true };
        Theme.Text2(dl, font, size, new Vector2(timerX, textY), Theme.U32(live ? Chrome.AccentBright(Config) : Theme.TextMuted), timer, false);
        if (live)
        {
            var pulse = plugin.ReducedMotion ? 1f : 0.55f + 0.45f * MathF.Sin((float)now * 4f);
            dl.AddCircleFilled(new Vector2(timerX - 7, textY + size * 0.55f), 3f, Theme.U32(Theme.Ember, pulse));
            timerX -= 12;
        }

        // "PvP", then the match: click it to pick a past one.
        const string label = "PvP";
        Theme.Text2(dl, font, size, new Vector2(x, textY), Theme.U32(Config.TextColor), label, false);
        x += Theme.Measure(font, size, label) + 8;

        var room = timerX - 10 - x;
        if (room > 24)
        {
            var text = match is null ? "No match yet" : fixedMatch is null ? match.Title : $"#{match.Id} {match.Title}";
            var fit = Theme.Fit(font, size * 0.86f, text, room);
            ImGui.SetCursorScreenPos(new Vector2(x - 2, min.Y + 2));
            if (ImGui.InvisibleButton("##pvp-match", new Vector2(Math.Max(Theme.Measure(font, size * 0.86f, fit) + 4, 1), h - 4)))
                ImGui.OpenPopup("##pvp-matches");
            var hover = ImGui.IsItemHovered();
            Theme.Text2(dl, font, size * 0.86f, new Vector2(x, textY + size * 0.08f), Theme.U32(hover ? Chrome.AccentBright(Config) : Theme.TextMuted), fit, false);
        }

        return h;
    }

    // ------------------------------------------------------------------
    // Body
    // ------------------------------------------------------------------

    private readonly record struct Column(PvpSort Sort, string Label, float Width, Func<Combatant, string> Value);

    private void DrawBody(Encounter match, Vector2 min, Vector2 max, double now)
    {
        var dl = ImGui.GetWindowDrawList();
        var numFont = plugin.Fonts.Get(Config.NumberFont);
        var nameFont = plugin.Fonts.Get(Config.NameFont);
        var width = max.X - min.X;
        var pad = 3f;

        var y = DrawScore(dl, match, new Vector2(min.X, min.Y + 4), max.X, numFont) + 4;

        // Columns from the right; the narrower the window, the fewer figures.
        var numSize = Math.Min(Config.NumberFontSize, MathF.Round(Config.BarHeight));
        var small = Theme.Measure(numFont, numSize, "88") + 14;
        var big = Theme.Measure(numFont, numSize, "888.8k") + 12;
        var columns = new List<Column>
        {
            new(PvpSort.Kills, "K", small, c => c.Kills.ToString()),
            new(PvpSort.Deaths, "D", small, c => c.Deaths.Count.ToString()),
            new(PvpSort.Assists, "A", small, c => c.Assists.ToString()),
        };
        if (width >= 300) columns.Add(new(PvpSort.Damage, "DMG", big, c => Format.Number(c.PlayerDamage, Config.NumberFormat)));
        if (width >= 380) columns.Add(new(PvpSort.Healing, "HEAL", big, c => Format.Number(c.Healing, Config.NumberFormat)));
        if (width >= 450) columns.Add(new(PvpSort.Taken, "TAKEN", big, c => Format.Number(c.DamageTaken, Config.NumberFormat)));

        var rows = Rows(match);
        var feed = Settings.ShowFeed ? match.Kills.TakeLast(FeedLines).Reverse().ToList() : [];
        var lineH = MathF.Round(Math.Min(Config.NameFontSize, 15f) + 5f);
        var feedH = feed.Count > 0 ? 8 + feed.Count * lineH : 0f;
        // The table keeps at least two rows; the feed gives way first.
        var barH = MathF.Round(Config.BarHeight);
        while (feed.Count > 0 && max.Y - feedH - y < 2 * (barH + Config.BarSpacing) + 20)
        {
            feed.RemoveAt(feed.Count - 1);
            feedH = feed.Count > 0 ? 8 + feed.Count * lineH : 0f;
        }

        // Column titles, each one a sort button.
        var headSize = Math.Min(Config.NumberFontSize * 0.75f, 13f);
        var right = max.X - pad - 6;
        var colRight = new float[columns.Count];
        for (var i = columns.Count - 1; i >= 0; i--)
        {
            colRight[i] = right;
            var col = columns[i];
            var w = Theme.Measure(numFont, headSize, col.Label);
            var p = new Vector2(right - col.Width + 2, y);
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"##sort{col.Sort}", new Vector2(col.Width - 2, headSize + 4)))
                Sort = col.Sort;
            var tint = Sort == col.Sort ? Chrome.AccentBright(Config) : ImGui.IsItemHovered() ? Config.TextColor : Theme.TextMuted;
            dl.AddText(numFont, headSize, new Vector2(right - w, y + 1), Theme.U32(tint), col.Label);
            if (ImGui.IsItemHovered()) Theme.Tooltip($"Sort by {SortName(col.Sort)}");
            right -= col.Width;
        }

        y += headSize + 6;

        if (rows.Count == 0)
        {
            DrawEmpty(dl, new Vector2(min.X, y), new Vector2(max.X, max.Y - feedH), "No hits on players yet");
        }
        else
        {
            var bottom = max.Y - feedH;
            var topDamage = Math.Max(rows.Max(c => c.PlayerDamage), 1);
            foreach (var c in rows)
            {
                if (y + barH > bottom + 1) break;
                var rowMin = new Vector2(min.X + pad, y);
                var rowMax = new Vector2(max.X - pad, y + barH);

                ImGui.SetCursorScreenPos(rowMin);
                ImGui.InvisibleButton($"##pvp{c.EntityId}", rowMax - rowMin);
                var hovered = ImGui.IsItemHovered();
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    plugin.OpenBreakdown(SegmentOf(match), c.EntityId, MeterMode.Damage);

                DrawRow(dl, rowMin, rowMax, c, match, columns, colRight, (float)c.PlayerDamage / topDamage, hovered, nameFont, numFont);
                if (hovered) RowTooltip(c, match);
                y += barH + Config.BarSpacing;
            }
        }

        if (feed.Count > 0)
            DrawFeed(dl, match, feed, new Vector2(min.X, max.Y - feedH), max, lineH, now);
    }

    private List<Combatant> Rows(Encounter match)
    {
        IEnumerable<Combatant> rows = match.Combatants.Values.Where(c => !c.IsChocobo && (c.PlayerDamage > 0 || c.Kills + c.Assists + c.Deaths.Count > 0 || c.Healing > 0));
        IOrderedEnumerable<Combatant> sorted = Sort switch
        {
            PvpSort.Deaths => rows.OrderByDescending(c => c.Deaths.Count),
            PvpSort.Assists => rows.OrderByDescending(c => c.Assists),
            PvpSort.Damage => rows.OrderByDescending(c => c.PlayerDamage),
            PvpSort.Healing => rows.OrderByDescending(c => c.Healing),
            PvpSort.Taken => rows.OrderByDescending(c => c.DamageTaken),
            _ => rows.OrderByDescending(c => c.Kills).ThenByDescending(c => c.Assists).ThenBy(c => c.Deaths.Count),
        };
        return sorted.ThenByDescending(c => c.PlayerDamage).ThenBy(c => c.EntityId).ToList();
    }

    private static string SortName(PvpSort sort) => sort switch
    {
        PvpSort.Kills => "kills",
        PvpSort.Deaths => "deaths",
        PvpSort.Assists => "assists",
        PvpSort.Damage => "damage to players",
        PvpSort.Healing => "healing",
        _ => "damage taken",
    };

    /// <summary>The team's score: knockouts we scored against the times we went down.</summary>
    private float DrawScore(ImDrawListPtr dl, Encounter match, Vector2 min, float right, ImFontPtr numFont)
    {
        var scored = match.Kills.Count(k => k.Ours);
        var lost = match.Kills.Count(k => !k.Ours);
        var size = MathF.Round(Config.NumberFontSize * 1.25f);
        var labelSize = Math.Min(Config.NumberFontSize * 0.75f, 13f);
        var shadow = Config.TextShadow;

        var x = min.X + 10;
        Theme.Text2(dl, numFont, size, new Vector2(x, min.Y), Theme.U32(Theme.Aether), scored.ToString(), shadow);
        x += Theme.Measure(numFont, size, scored.ToString()) + 6;
        Theme.Text2(dl, numFont, size, new Vector2(x, min.Y), Theme.U32(Theme.TextFaint), "–", shadow);
        x += Theme.Measure(numFont, size, "–") + 6;
        Theme.Text2(dl, numFont, size, new Vector2(x, min.Y), Theme.U32(Theme.Ember), lost.ToString(), shadow);
        x += Theme.Measure(numFont, size, lost.ToString()) + 10;
        dl.AddText(numFont, labelSize, new Vector2(x, min.Y + size - labelSize - 1), Theme.U32(Theme.TextMuted), "KNOCKOUTS");

        var damage = $"{Format.Number(match.TotalPlayerDamage, Config.NumberFormat)} to players";
        var dw = Theme.Measure(numFont, labelSize, damage);
        if (right - 10 - dw > x + Theme.Measure(numFont, labelSize, "KNOCKOUTS") + 12)
            dl.AddText(numFont, labelSize, new Vector2(right - 10 - dw, min.Y + size - labelSize - 1), Theme.U32(Theme.TextMuted), damage);

        return min.Y + size + 2;
    }

    private void DrawRow(ImDrawListPtr dl, Vector2 min, Vector2 max, Combatant c, Encounter match, List<Column> columns, float[] colRight,
                         float fill, bool hovered, ImFontPtr nameFont, ImFontPtr numFont)
    {
        var h = max.Y - min.Y;
        var rounding = Math.Min(Config.Rounding, h * 0.5f);

        // The bar is damage to players, kept faint so the figures read over it.
        Bars.Track(dl, min, max, rounding, Config.BarOpacity);
        Bars.Fill(dl, min, max, fill, JobColors.For(c, Config), Config.BarStyle, rounding, Config.BarOpacity * 0.55f, hovered);
        if (Config.HighlightSelf && c.IsLocalPlayer)
            dl.AddRect(min, max, Theme.U32(Chrome.AccentBright(Config), 0.55f), rounding, ImDrawFlags.None, 1f);
        if (hovered)
            dl.AddRect(min, max, Theme.U32(Theme.Text, 0.35f), rounding, ImDrawFlags.None, 1f);

        var x = min.X + 4;
        if (Config.ShowJobIcons && GameData.Profile(c).Icon is var iconId and not 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            var icon = h - 2;
            dl.AddImage(wrap.Handle, new Vector2(x - 2, min.Y + 1), new Vector2(x - 2 + icon, min.Y + 1 + icon));
            x += icon + 2;
        }

        var shadow = Config.TextShadow;
        var numSize = Math.Min(Config.NumberFontSize, h);
        var nameSize = Math.Min(Config.NameFontSize, h - 2);
        var numY = min.Y + (h - numSize) * 0.5f;

        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var text = col.Value(c);
            var color = col.Sort switch
            {
                PvpSort.Kills when c.Kills > 0 => Theme.AetherBright,
                PvpSort.Deaths when c.Deaths.Count > 0 => Theme.Ember,
                _ => Config.TextColor,
            };
            var alpha = col.Sort is PvpSort.Kills or PvpSort.Deaths or PvpSort.Assists ? 1f : 0.8f;
            Theme.Text2(dl, numFont, numSize, new Vector2(colRight[i] - Theme.Measure(numFont, numSize, text), numY), Theme.U32(color, alpha), text, shadow);
        }

        var nameRight = colRight[0] - columns[0].Width;
        var name = Theme.Fit(nameFont, nameSize, plugin.Names.For(c, match), nameRight - 6 - x);
        Theme.Text2(dl, nameFont, nameSize, new Vector2(x, min.Y + (h - nameSize) * 0.5f - 0.5f), Theme.U32(Config.TextColor), name, shadow);
    }

    private void RowTooltip(Combatant c, Encounter match)
    {
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        ImGui.BeginTooltip();

        var job = GameData.Profile(c);
        ImGui.TextColored(JobColors.For(c, Config), plugin.Names.For(c, match));
        if (job.Name.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, job.Name);
        }

        ImGui.Separator();
        var scored = match.Kills.Count(k => k.Ours);
        var share = scored > 0 ? (double)(c.Kills + c.Assists) / scored : 0;
        ImGui.TextUnformatted($"{c.Kills} / {c.Deaths.Count} / {c.Assists}   ·   KDA {c.Kda:0.0}");
        ImGui.TextColored(Theme.TextMuted, $"Kill participation {Format.Percent(Math.Min(share, 1))}");
        ImGui.TextColored(Theme.TextMuted, $"Damage to players {Format.Number(c.PlayerDamage, Config.NumberFormat)}   ·   all damage {Format.Number(c.Damage, Config.NumberFormat)}");
        ImGui.TextColored(Theme.TextMuted, $"Healing {Format.Number(c.Healing, Config.NumberFormat)}   ·   taken {Format.Number(c.DamageTaken, Config.NumberFormat)}");
        ImGui.TextColored(Theme.TextFaint, "Click for the full breakdown");
        ImGui.EndTooltip();
    }

    // ------------------------------------------------------------------
    // Kill feed
    // ------------------------------------------------------------------

    private void DrawFeed(ImDrawListPtr dl, Encounter match, List<KillRecord> feed, Vector2 min, Vector2 max, float lineH, double now)
    {
        dl.AddLine(new Vector2(min.X + 8, min.Y + 3), new Vector2(max.X - 8, min.Y + 3), Theme.U32(Theme.LineSoft), 1f);
        var font = plugin.Fonts.Get(Config.NameFont);
        var numFont = plugin.Fonts.Get(Config.NumberFont);
        var size = lineH - 5f;
        var shadow = Config.TextShadow;
        var y = min.Y + 7;

        foreach (var k in feed)
        {
            var x = min.X + 10;
            var time = Format.Duration(k.Time - match.Start);
            Theme.Text2(dl, numFont, size, new Vector2(x, y), Theme.U32(Theme.TextFaint), time, shadow);
            x += Theme.Measure(numFont, size, "00:00") + 8;

            var killer = KillerName(k, match);
            var victim = VictimName(k, match);
            var (us, them) = (Theme.Aether, Theme.Ember);
            var assists = k.Assists.Length > 0 ? $"+{k.Assists.Length}" : string.Empty;
            var assistsW = assists.Length > 0 ? Theme.Measure(numFont, size, assists) + 8 : 0;
            var room = max.X - 10 - assistsW - x;
            var arrow = "  ›  ";
            var arrowW = Theme.Measure(font, size, arrow);
            var half = Math.Max((room - arrowW) * 0.5f, 10);

            var start = x;
            if (killer.Length > 0)
            {
                var fit = Theme.Fit(font, size, killer, half);
                Theme.Text2(dl, font, size, new Vector2(x, y), Theme.U32(k.Ours ? us : them), fit, shadow);
                x += Theme.Measure(font, size, fit);
                Theme.Text2(dl, font, size, new Vector2(x, y), Theme.U32(Theme.TextMuted), arrow, shadow);
                x += arrowW;
            }

            var victimFit = Theme.Fit(font, size, victim, max.X - 10 - assistsW - x);
            Theme.Text2(dl, font, size, new Vector2(x, y), Theme.U32(k.Ours ? them : us, 0.9f), victimFit, shadow);
            x += Theme.Measure(font, size, victimFit);
            if (killer.Length == 0)
                Theme.Text2(dl, font, size, new Vector2(x, y), Theme.U32(Theme.TextMuted), " down", shadow);
            if (assists.Length > 0)
                Theme.Text2(dl, numFont, size, new Vector2(max.X - 10 - assistsW + 8, y), Theme.U32(Theme.TextMuted), assists, shadow);

            if (ImGui.IsMouseHoveringRect(new Vector2(start, y), new Vector2(max.X, y + lineH)) && ImGui.IsWindowHovered())
                FeedTooltip(k, match);
            y += lineH;
        }
    }

    private void FeedTooltip(KillRecord k, Encounter match)
    {
        var lines = new List<string>
        {
            k.Ours ? $"{VictimName(k, match)} knocked out" : $"{VictimName(k, match)} went down",
        };
        var killer = KillerName(k, match);
        lines.Add(killer.Length > 0 ? $"Killing blow: {killer}" : "Killing blow: someone outside the meter");
        if (k.Assists.Length > 0)
            lines.Add("Assists: " + string.Join(", ", k.Assists.Select(id => match.Combatants.TryGetValue(id, out var c) ? plugin.Names.For(c, match) : "?")));
        lines.Add($"at {Format.Duration(k.Time - match.Start)}");
        Theme.Tooltip(string.Join("\n", lines));
    }

    /// <summary>Ours go through the segment's names; opponents through the same streamer rules.</summary>
    private string KillerName(KillRecord k, Encounter match)
    {
        if (k.Ours)
            return match.Combatants.TryGetValue(k.KillerId, out var c) ? plugin.Names.For(c, match) : string.Empty;
        return k.Killer.Length > 0 ? plugin.Names.Opponent(k.Killer, k.KillerJob) : string.Empty;
    }

    private string VictimName(KillRecord k, Encounter match)
    {
        if (k.Ours) return plugin.Names.Opponent(k.Victim, k.VictimJob);
        return match.Combatants.TryGetValue(k.VictimId, out var c) ? plugin.Names.For(c, match) : k.Victim;
    }

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
    // Menus
    // ------------------------------------------------------------------

    private void DrawPopups()
    {
        using (var matches = ImRaii.Popup("##pvp-matches"))
        {
            if (matches)
            {
                if (ImGui.MenuItem("Latest match", string.Empty, fixedMatch is null)) fixedMatch = null;
                var past = plugin.Encounters.History.Where(e => e.IsPvp).ToList();
                if (past.Count > 0) ImGui.Separator();
                foreach (var e in past)
                    if (ImGui.MenuItem($"#{e.Id} {e.Title}  ·  {Format.Duration(e.Duration(plugin.Now))}  ·  {e.StartedAt:HH:mm}", string.Empty, fixedMatch == e.Id))
                        fixedMatch = e.Id;
            }
        }

        using var popup = ImRaii.Popup("##pvp-menu");
        if (!popup) return;

        ImGui.TextColored(Theme.TextFaint, "SORT BY");
        foreach (var sort in Enum.GetValues<PvpSort>())
            if (ImGui.MenuItem(char.ToUpperInvariant(SortName(sort)[0]) + SortName(sort)[1..], string.Empty, Sort == sort))
                Sort = sort;

        ImGui.Separator();
        Toggle("Kill feed", Settings.ShowFeed, v => Settings.ShowFeed = v);
        Toggle("Only in PvP", Settings.OnlyInPvp, v => Settings.OnlyInPvp = v);
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
