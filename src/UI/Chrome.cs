using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Aethermeter.Models;

namespace Aethermeter.UI;

/// <summary>
/// The configurable frame shared by the borderless windows (meters and party cooldowns):
/// background, border, header panel and watermark, all from the Look settings.
/// </summary>
public static class Chrome
{
    public static float Radius(Configuration c) => Math.Max(c.WindowRounding, 0f);
    public static Vector4 AccentBright(Configuration c) => Theme.Lighten(c.WindowAccent, 0.4f);

    /// <summary>Pushes the window's padding, rounding, border and colour. Call after <see cref="Theme.Push"/>, before Begin.</summary>
    public static IDisposable Push(Configuration c, Vector2 minSize)
    {
        var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero)
            .Push(ImGuiStyleVar.WindowRounding, Radius(c))
            .Push(ImGuiStyleVar.WindowMinSize, minSize)
            .Push(ImGuiStyleVar.WindowBorderSize, c.ShowBorder ? c.BorderThickness : 0f);
        var colors = ImRaii.PushColor(ImGuiCol.WindowBg, c.BackgroundColor with { W = 1f })
            .Push(ImGuiCol.Border, c.BorderColor);
        return new Scope(colors, style);
    }

    private sealed class Scope(IDisposable a, IDisposable b) : IDisposable
    {
        public void Dispose()
        {
            a.Dispose();
            b.Dispose();
        }
    }

    /// <summary>The header strip: its panel and, if on, the accent line along the bottom.</summary>
    public static void Header(ImDrawListPtr dl, Configuration c, Vector2 min, Vector2 max)
    {
        dl.AddRectFilled(min, max, Theme.U32(c.HeaderColor, c.HeaderOpacity), Radius(c), ImDrawFlags.RoundCornersTop);
        if (c.ShowHeaderLine)
            dl.AddRectFilledMultiColor(new Vector2(min.X, max.Y - 1), max,
                Theme.U32(c.WindowAccent, 0.55f), Theme.U32(c.WindowAccent, 0.0f), Theme.U32(c.WindowAccent, 0.0f), Theme.U32(c.WindowAccent, 0.55f));
    }

    /// <summary>
    /// The chosen background style, laid over the window colour behind the content. Every style
    /// fades to nothing at the bottom, so the window's rounded corners stay clean.
    /// </summary>
    public static void Backdrop(ImDrawListPtr dl, Configuration c, Vector2 min, Vector2 max)
    {
        var inset = c.ShowBorder ? c.BorderThickness : 0f;
        min.X += inset;
        max.X -= inset;
        var h = max.Y - min.Y;
        if (h <= 0 || max.X <= min.X) return;

        var op = Math.Max(c.BackgroundOpacity, 0.3f);
        switch (c.BackgroundStyle)
        {
            case BackgroundStyle.Gradient:
                var light = Theme.Lighten(c.BackgroundColor, 0.18f);
                dl.AddRectFilledMultiColor(min, max, Theme.U32(light, 0.5f * op), Theme.U32(light, 0.5f * op), Theme.U32(light, 0f), Theme.U32(light, 0f));
                break;

            case BackgroundStyle.Glass:
                var white = new Vector4(1, 1, 1, 1);
                dl.AddRectFilledMultiColor(min, new Vector2(max.X, min.Y + h * 0.6f),
                    Theme.U32(white, 0.07f * op), Theme.U32(white, 0.02f * op), Theme.U32(white, 0f), Theme.U32(white, 0f));
                break;

            case BackgroundStyle.Glow:
                var accent = c.WindowAccent;
                dl.AddRectFilledMultiColor(min, new Vector2(max.X, min.Y + h * 0.45f),
                    Theme.U32(accent, 0.16f * op), Theme.U32(accent, 0.16f * op), Theme.U32(accent, 0f), Theme.U32(accent, 0f));
                break;
        }
    }

    /// <summary>"AETHERMETER" over "by GitHixy", faint in the middle of the window, behind the content.</summary>
    /// <returns>Half the height it took, or 0 when it isn't shown.</returns>
    public static float Watermark(ImDrawListPtr dl, ImFontPtr font, Configuration c, Vector2 min, Vector2 max)
    {
        if (!c.ShowWatermark || c.WatermarkOpacity <= 0f) return 0f;

        const string title = "AETHERMETER";
        const string by = "by GitHixy";
        var unit = Theme.Measure(font, 1f, title);
        var room = max.Y - min.Y;
        if (unit <= 0f) return 0f;

        var size = Math.Clamp((max.X - min.X) * 0.6f / unit, 10f, 36f);
        if (room < size) return 0f;
        var subSize = size * 0.45f;
        var withBy = subSize >= 8f && room >= size + subSize + 2f;
        var block = withBy ? size + subSize + 2f : size;

        var center = (min + max) * 0.5f;
        var top = center.Y - block * 0.5f;
        var color = Theme.U32(c.TextColor, c.WatermarkOpacity);
        dl.AddText(font, size, new Vector2(center.X - Theme.Measure(font, size, title) * 0.5f, top), color, title);
        if (withBy)
            dl.AddText(font, subSize, new Vector2(center.X - Theme.Measure(font, subSize, by) * 0.5f, top + size + 2f), color, by);
        return block * 0.5f;
    }
}
