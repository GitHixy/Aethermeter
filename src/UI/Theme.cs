using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Aethermeter.UI;

/// <summary>
/// Aethermeter's look: smoked obsidian glass with an aether-teal accent and an ember counter-accent
/// for warnings and deaths. Numbers are set in a condensed face so columns line up tight.
/// </summary>
public static class Theme
{
    // ------------------------------------------------------------------
    // Palette
    // ------------------------------------------------------------------

    public static readonly Vector4 Ink = Rgb(0x0C, 0x0E, 0x12, 0.97f);
    public static readonly Vector4 Panel = Rgb(0x13, 0x16, 0x1C);
    public static readonly Vector4 Surface = Rgb(0x1A, 0x1E, 0x26);
    public static readonly Vector4 SurfaceHover = Rgb(0x23, 0x29, 0x34);
    public static readonly Vector4 SurfaceActive = Rgb(0x2C, 0x34, 0x42);

    public static readonly Vector4 Line = Rgb(0x2A, 0x30, 0x3C);
    public static readonly Vector4 LineSoft = Rgb(0x1E, 0x23, 0x2C);

    /// <summary>Aether teal: selection, focus and the brand mark.</summary>
    public static readonly Vector4 Aether = Rgb(0x5E, 0xD9, 0xC7);
    public static readonly Vector4 AetherBright = Rgb(0x9C, 0xF0, 0xE2);
    public static readonly Vector4 AetherDim = Rgb(0x2B, 0x6B, 0x63);

    /// <summary>Ember: deaths, resets and anything destructive.</summary>
    public static readonly Vector4 Ember = Rgb(0xF0, 0x7D, 0x4F);
    public static readonly Vector4 Gold = Rgb(0xE8, 0xC2, 0x6A);

    public static readonly Vector4 Text = Rgb(0xE6, 0xE8, 0xEC);
    public static readonly Vector4 TextMuted = Rgb(0x96, 0x9E, 0xAC);
    public static readonly Vector4 TextFaint = Rgb(0x5E, 0x66, 0x74);

    public static readonly Vector4 PatreonCoral = Rgb(0xFF, 0x42, 0x4D);
    public static readonly Vector4 DiscordBlurple = Rgb(0x58, 0x65, 0xF2);

    public const float Radius = 5f;

    private static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    public static Vector4 Alpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);
    public static uint U32(Vector4 c, float a = 1f) => ImGui.GetColorU32(Alpha(c, a));
    public static Vector4 Lighten(Vector4 c, float t) => new(c.X + (1f - c.X) * t, c.Y + (1f - c.Y) * t, c.Z + (1f - c.Z) * t, c.W);
    public static Vector4 Darken(Vector4 c, float t) => new(c.X * (1f - t), c.Y * (1f - t), c.Z * (1f - t), c.W);

    /// <summary>Perceived brightness, to choose dark or light text over a bar.</summary>
    public static float Luma(Vector4 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    // ------------------------------------------------------------------
    // Global style
    // ------------------------------------------------------------------

    /// <summary>Pushes the full look. Call before ImGui.Begin so the window chrome picks it up.</summary>
    public static IDisposable Push()
    {
        var colors = ImRaii.PushColor(ImGuiCol.WindowBg, Ink)
            .Push(ImGuiCol.ChildBg, Vector4.Zero)
            .Push(ImGuiCol.PopupBg, Rgb(0x11, 0x14, 0x19, 0.98f))
            .Push(ImGuiCol.Border, Line)
            .Push(ImGuiCol.BorderShadow, Vector4.Zero)
            .Push(ImGuiCol.Text, Text)
            .Push(ImGuiCol.TextDisabled, TextFaint)
            .Push(ImGuiCol.TitleBg, Rgb(0x0E, 0x10, 0x15, 0.98f))
            .Push(ImGuiCol.TitleBgActive, Rgb(0x14, 0x18, 0x1F, 0.98f))
            .Push(ImGuiCol.TitleBgCollapsed, Rgb(0x0E, 0x10, 0x15, 0.8f))
            .Push(ImGuiCol.FrameBg, Surface)
            .Push(ImGuiCol.FrameBgHovered, SurfaceHover)
            .Push(ImGuiCol.FrameBgActive, SurfaceActive)
            .Push(ImGuiCol.Button, Surface)
            .Push(ImGuiCol.ButtonHovered, SurfaceHover)
            .Push(ImGuiCol.ButtonActive, SurfaceActive)
            .Push(ImGuiCol.Header, Alpha(Aether, 0.14f))
            .Push(ImGuiCol.HeaderHovered, Alpha(Aether, 0.22f))
            .Push(ImGuiCol.HeaderActive, Alpha(Aether, 0.3f))
            .Push(ImGuiCol.Separator, Line)
            .Push(ImGuiCol.CheckMark, Aether)
            .Push(ImGuiCol.SliderGrab, Aether)
            .Push(ImGuiCol.SliderGrabActive, AetherBright)
            .Push(ImGuiCol.ResizeGrip, Vector4.Zero)
            .Push(ImGuiCol.ResizeGripHovered, Alpha(Aether, 0.5f))
            .Push(ImGuiCol.ResizeGripActive, Alpha(Aether, 0.8f))
            .Push(ImGuiCol.TableHeaderBg, Rgb(0x15, 0x19, 0x20))
            .Push(ImGuiCol.TableBorderStrong, Line)
            .Push(ImGuiCol.TableBorderLight, LineSoft)
            .Push(ImGuiCol.TableRowBg, Vector4.Zero)
            .Push(ImGuiCol.TableRowBgAlt, Alpha(Surface, 0.4f))
            .Push(ImGuiCol.ScrollbarBg, Vector4.Zero)
            .Push(ImGuiCol.ScrollbarGrab, Alpha(Line, 1.3f))
            .Push(ImGuiCol.ScrollbarGrabHovered, Alpha(Aether, 0.45f))
            .Push(ImGuiCol.ScrollbarGrabActive, Alpha(Aether, 0.7f))
            .Push(ImGuiCol.NavHighlight, Alpha(Aether, 0.6f));

        var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 7f)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(14, 12))
            .Push(ImGuiStyleVar.FrameRounding, Radius)
            .Push(ImGuiStyleVar.FrameBorderSize, 0f)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(8, 4))
            .Push(ImGuiStyleVar.PopupRounding, Radius)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6))
            .Push(ImGuiStyleVar.ScrollbarSize, 9f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 5f)
            .Push(ImGuiStyleVar.GrabMinSize, 10f)
            .Push(ImGuiStyleVar.GrabRounding, 4f)
            .Push(ImGuiStyleVar.CellPadding, new Vector2(7, 4));

        return new Scope(colors, styles);
    }

    private sealed class Scope(params IDisposable[] parts) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (var i = parts.Length - 1; i >= 0; i--)
                parts[i].Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Text
    // ------------------------------------------------------------------

    /// <summary>Width of a string in a font at an arbitrary pixel size.</summary>
    public static unsafe float Measure(ImFontPtr font, float size, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var k = size / font.FontSize;
        var width = 0f;
        foreach (var c in text)
        {
            var glyph = font.FindGlyph(c);
            if (glyph != null) width += glyph->AdvanceX * k;
        }

        return width;
    }

    /// <summary>Draws text with an optional soft drop shadow, which keeps it legible over bright bars.</summary>
    public static void Text2(ImDrawListPtr dl, ImFontPtr font, float size, Vector2 pos, uint color, string text, bool shadow, float alpha = 1f)
    {
        if (shadow)
            dl.AddText(font, size, pos + new Vector2(1, 1), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.75f * alpha)), text);
        dl.AddText(font, size, pos, color, text);
    }

    /// <summary>Trims text with an ellipsis so it fits a width.</summary>
    public static string Fit(ImFontPtr font, float size, string text, float maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;
        if (Measure(font, size, text) <= maxWidth) return text;

        var budget = maxWidth - Measure(font, size, "…");
        var length = text.Length;
        while (length > 1 && Measure(font, size, text[..length]) > budget)
            length--;
        return text[..length].TrimEnd() + "…";
    }

    // ------------------------------------------------------------------
    // Widgets
    // ------------------------------------------------------------------

    /// <summary>A borderless glyph button for toolbars. Returns true when clicked.</summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, Vector4? tint = null, float size = 0)
    {
        var h = size > 0 ? size : ImGui.GetFrameHeight();
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(h, h));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(p, p + new Vector2(h, h), U32(active ? SurfaceActive : SurfaceHover), Radius);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var glyph = icon.ToIconString();
            var gs = ImGui.CalcTextSize(glyph);
            var scale = Math.Min(1f, (h - 6) / Math.Max(gs.Y, 1));
            var gsz = gs * scale;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, p + (new Vector2(h, h) - gsz) * 0.5f,
                U32(hovered ? AetherBright : tint ?? TextMuted), glyph);
        }

        if (hovered && tooltip.Length > 0) Tooltip(tooltip);
        return clicked;
    }

    public static void Tooltip(string text)
    {
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(9, 7));
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>A small grey "?" that explains the setting before it.</summary>
    public static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) Tooltip(text);
    }

    /// <summary>Section label: small caps in the accent with a fading rule after it.</summary>
    public static void Section(string label)
    {
        ImGui.Dummy(new Vector2(0, 4));
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight();

        dl.AddRectFilled(new Vector2(p.X, p.Y + 2), new Vector2(p.X + 3, p.Y + h - 2), U32(Aether), 1.5f);
        ImGui.SetCursorScreenPos(new Vector2(p.X + 10, p.Y));
        ImGui.TextColored(AetherBright, label.ToUpperInvariant());

        var after = ImGui.GetItemRectMax().X + 10;
        var right = p.X + ImGui.GetContentRegionAvail().X;
        if (right > after)
        {
            var y = p.Y + h * 0.5f;
            dl.AddRectFilledMultiColor(new Vector2(after, y), new Vector2(right, y + 1),
                U32(Aether, 0.35f), U32(Aether, 0f), U32(Aether, 0f), U32(Aether, 0.35f));
        }

        ImGui.Dummy(new Vector2(0, 2));
    }

    /// <summary>
    /// A row of pill tabs with glyphs, wrapping when the window is narrow.
    /// Returns true when the selection changed.
    /// </summary>
    public static bool Tabs(string id, ref int selected, params (FontAwesomeIcon Icon, string Label)[] tabs)
    {
        var dl = ImGui.GetWindowDrawList();
        var left = ImGui.GetCursorScreenPos().X;
        var width = ImGui.GetContentRegionAvail().X;
        var h = ImGui.GetFrameHeight() + 4f;
        var x = left;
        var y = ImGui.GetCursorScreenPos().Y;
        var changed = false;

        for (var i = 0; i < tabs.Length; i++)
        {
            var (icon, label) = tabs[i];
            float iconW;
            using (ImRaii.PushFont(UiBuilder.IconFont)) iconW = ImGui.CalcTextSize(icon.ToIconString()).X;
            var w = iconW + ImGui.CalcTextSize(label).X + 30f;
            if (x + w > left + width && x > left)
            {
                x = left;
                y += h + 4f;
            }

            ImGui.SetCursorScreenPos(new Vector2(x, y));
            if (ImGui.InvisibleButton($"{id}{i}", new Vector2(w, h)) && selected != i)
            {
                selected = i;
                changed = true;
            }

            var hovered = ImGui.IsItemHovered();
            var isSel = selected == i;
            var min = new Vector2(x, y);
            var max = new Vector2(x + w, y + h);

            if (isSel)
            {
                dl.AddRectFilled(min, max, U32(Aether, 0.13f), Radius);
                dl.AddRectFilled(new Vector2(min.X + 9, max.Y - 2.5f), new Vector2(max.X - 9, max.Y - 0.5f), U32(Aether), 1f);
            }
            else if (hovered)
                dl.AddRectFilled(min, max, U32(SurfaceHover), Radius);

            var tint = U32(isSel ? AetherBright : hovered ? Text : TextMuted);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var glyph = icon.ToIconString();
                var gs = ImGui.CalcTextSize(glyph);
                dl.AddText(new Vector2(min.X + 11, min.Y + (h - gs.Y) * 0.5f), tint, glyph);
            }

            var ts = ImGui.CalcTextSize(label);
            dl.AddText(new Vector2(min.X + 19 + iconW, min.Y + (h - ts.Y) * 0.5f), tint, label);
            x += w + 4f;
        }

        ImGui.SetCursorScreenPos(new Vector2(left, y + h + 6));
        ImGui.Dummy(new Vector2(width, 0));
        return changed;
    }

    /// <summary>A hairline across the region that fades out at both ends.</summary>
    public static void Rule()
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var mid = p.X + w * 0.5f;
        dl.AddRectFilledMultiColor(p, new Vector2(mid, p.Y + 1), U32(Aether, 0f), U32(Aether, 0.4f), U32(Aether, 0.4f), U32(Aether, 0f));
        dl.AddRectFilledMultiColor(new Vector2(mid, p.Y), new Vector2(p.X + w, p.Y + 1), U32(Aether, 0.4f), U32(Aether, 0f), U32(Aether, 0f), U32(Aether, 0.4f));
        ImGui.Dummy(new Vector2(w, 10));
    }

    /// <summary>A rounded button: filled in its colour, or outlined ("ghost"). Width 0 fits the label.</summary>
    public static bool PillButton(string label, Vector2 size, Vector4 color, bool filled = false, FontAwesomeIcon? icon = null)
    {
        string? glyph = icon?.ToIconString();
        float glyphW = 0;
        if (glyph is not null)
            using (ImRaii.PushFont(UiBuilder.IconFont)) glyphW = ImGui.CalcTextSize(glyph).X + 7;

        var ts = ImGui.CalcTextSize(label);
        var h = size.Y > 0 ? size.Y : ImGui.GetFrameHeight() + 4;
        var w = size.X > 0 ? size.X : ts.X + glyphW + 26;
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##pill" + label, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var max = p + new Vector2(w, h);
        if (filled)
        {
            dl.AddRectFilled(p, max, U32(hovered ? Lighten(color, 0.15f) : color, 0.9f), h * 0.5f);
        }
        else
        {
            dl.AddRectFilled(p, max, U32(color, hovered ? 0.2f : 0.08f), h * 0.5f);
            dl.AddRect(p, max, U32(color, hovered ? 0.9f : 0.55f), h * 0.5f, ImDrawFlags.None, 1f);
        }

        var tint = filled ? U32(Luma(color) > 0.55f ? Darken(color, 0.82f) : Text) : U32(hovered ? Lighten(color, 0.35f) : color);
        var x = p.X + (w - ts.X - glyphW) * 0.5f;
        if (glyph is not null)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var gs = ImGui.CalcTextSize(glyph);
                dl.AddText(new Vector2(x, p.Y + (h - gs.Y) * 0.5f), tint, glyph);
            }

            x += glyphW;
        }

        dl.AddText(new Vector2(x, p.Y + (h - ts.Y) * 0.5f), tint, label);
        return clicked;
    }

    /// <summary>Patreon, Discord and GitHub, side by side.</summary>
    public static void CommunityButtons(Plugin plugin)
    {
        if (PillButton("Patreon", Vector2.Zero, PatreonCoral, icon: FontAwesomeIcon.Heart)) plugin.OpenUrl(Plugin.PatreonUrl);
        ImGui.SameLine(0, 8);
        if (PillButton("Discord", Vector2.Zero, DiscordBlurple, icon: FontAwesomeIcon.Comments)) plugin.OpenUrl(Plugin.DiscordUrl);
        ImGui.SameLine(0, 8);
        if (PillButton("GitHub", Vector2.Zero, TextMuted, icon: FontAwesomeIcon.CodeBranch)) plugin.OpenUrl(Plugin.RepoUrl);
    }

    /// <summary>The Aethermeter mark: three rising bars in a rounded tile.</summary>
    public static void Logo(ImDrawListPtr dl, Vector2 min, float size, float alpha = 1f, Vector4? accent = null)
    {
        var tint = accent ?? Aether;
        var bright = accent is { } a ? Lighten(a, 0.4f) : AetherBright;
        var max = min + new Vector2(size, size);
        dl.AddRectFilled(min, max, U32(Alpha(tint, 0.16f), alpha), size * 0.24f);
        dl.AddRect(min, max, U32(Alpha(tint, 0.6f), alpha), size * 0.24f, ImDrawFlags.None, 1f);

        var pad = size * 0.24f;
        var bw = (size - pad * 2) / 3.6f;
        float[] heights = [0.42f, 0.68f, 1f];
        for (var i = 0; i < 3; i++)
        {
            var bx = min.X + pad + i * bw * 1.3f;
            var top = max.Y - pad - (size - pad * 2) * heights[i];
            dl.AddRectFilled(new Vector2(bx, top), new Vector2(bx + bw, max.Y - pad), U32(i == 2 ? bright : tint, alpha), 1f);
        }
    }
}
