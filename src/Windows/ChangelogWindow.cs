using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Aethermeter.Models;
using Aethermeter.UI;

namespace Aethermeter.Windows;

/// <summary>The "What's new" panel: shown once after an update, and on demand from the settings.</summary>
public sealed class ChangelogWindow : Window
{
    private readonly Plugin plugin;
    private IReadOnlyList<Release> releases = Array.Empty<Release>();

    public ChangelogWindow(Plugin plugin) : base("Aethermeter What's New")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 520);
        SizeMin = new Vector2(440, 320);
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        CenterOnAppearing = true;
    }

    protected override string Title() => "What's new in Aethermeter";

    /// <summary>Opens the notes published since the given version, if there are any.</summary>
    public void ShowSince(string lastSeenVersion)
    {
        releases = Changelog.Since(lastSeenVersion);
        if (releases.Count > 0) IsOpen = true;
    }

    public void ShowAll()
    {
        releases = Changelog.Releases;
        IsOpen = true;
    }

    protected override void DrawContents()
    {
        var current = Changelog.CurrentVersion;
        Masthead(current);

        var footer = ImGui.GetFrameHeight() + 18f;
        using (var body = ImRaii.Child("##notes", new Vector2(0, ImGui.GetContentRegionAvail().Y - footer)))
        {
            if (body)
            {
                if (releases.Count == 0)
                    ImGui.TextColored(Theme.TextMuted, "You're up to date: nothing new since your last visit.");

                for (var i = 0; i < releases.Count; i++)
                    DrawRelease(releases[i], i == 0);
            }
        }

        ImGui.Dummy(new Vector2(0, 4));
        if (Theme.PillButton("Got it", new Vector2(120, 0), Theme.Aether, filled: true))
            Dismiss(current);
        ImGui.SameLine(0, 8);
        Theme.CommunityButtons(plugin);
    }

    private void Masthead(string current)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        Theme.Logo(dl, p, 34);
        var font = plugin.Fonts.Get(FontId.BarlowCondensed);
        dl.AddText(font, 24f, p + new Vector2(46, -1), Theme.U32(Theme.Text), "WHAT'S NEW");
        var sub = releases.Count > 1 ? $"You're on {current}. Here's everything since your last update." : $"Version {current}";
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f, p + new Vector2(47, 22), Theme.U32(Theme.TextMuted), sub);
        ImGui.Dummy(new Vector2(0, 40));
        Theme.Rule();
    }

    private static void DrawRelease(Release release, bool latest)
    {
        var dl = ImGui.GetWindowDrawList();

        // Version plate: teal for the release you just moved to, muted for older ones.
        var accent = latest ? Theme.Aether : Theme.TextMuted;
        var plate = $"v{release.Version}";
        var p = ImGui.GetCursorScreenPos();
        var ps = ImGui.CalcTextSize(plate);
        dl.AddRectFilled(p, p + ps + new Vector2(14, 6), Theme.U32(accent, latest ? 0.22f : 0.12f), 4f);
        dl.AddRect(p, p + ps + new Vector2(14, 6), Theme.U32(accent, 0.6f), 4f);
        dl.AddText(p + new Vector2(7, 3), Theme.U32(latest ? Theme.AetherBright : Theme.TextMuted), plate);
        ImGui.SetCursorScreenPos(p + new Vector2(ps.X + 24, 3));
        ImGui.TextColored(latest ? Theme.Text : Theme.TextMuted, release.Headline);
        ImGui.SetCursorScreenPos(p + new Vector2(0, ps.Y + 14));

        // Tags share one column so every sentence starts on the same left edge.
        var tagWidth = 0f;
        foreach (var note in release.Notes)
            tagWidth = Math.Max(tagWidth, ImGui.CalcTextSize(Changelog.KindLabel(note.Kind)).X);
        tagWidth += 14f;

        foreach (var note in release.Notes)
        {
            var origin = ImGui.GetCursorScreenPos();
            var label = Changelog.KindLabel(note.Kind);
            var color = Changelog.KindColor(note.Kind);
            var lh = ImGui.GetTextLineHeight();

            var tagMin = origin;
            var tagMax = origin + new Vector2(tagWidth, lh + 2);
            dl.AddRectFilled(tagMin, tagMax, Theme.U32(color, 0.14f), 3f);
            var ls = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(origin.X + (tagWidth - ls.X) * 0.5f, origin.Y + 1), Theme.U32(color), label);

            ImGui.SetCursorScreenPos(new Vector2(origin.X + tagWidth + 10, origin.Y + 1));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(Theme.Text, note.Text);
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0, 3));
        }

        ImGui.Dummy(new Vector2(0, 8));
    }

    private void Dismiss(string current)
    {
        plugin.Configuration.LastSeenVersion = current;
        plugin.ConfigService.Save();
        IsOpen = false;
    }

    /// <summary>Closing with the title-bar button counts as read, so an update never nags twice.</summary>
    protected override void OnClosed() => Dismiss(Changelog.CurrentVersion);
}
