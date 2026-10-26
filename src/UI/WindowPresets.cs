using System.Numerics;
using Aethermeter.Models;

namespace Aethermeter.UI;

/// <summary>
/// Ready-made looks for the meter window. A preset sets the window chrome and bar style only:
/// bar colours, fonts and layout stay as the user left them.
/// </summary>
public static class WindowPresets
{
    public readonly record struct Preset(
        string Name,
        Vector4 Background,
        BackgroundStyle Style,
        float Opacity,
        Vector4 Header,
        float HeaderOpacity,
        Vector4 Accent,
        bool Border,
        Vector4 BorderColor,
        float Rounding,
        BarStyle Bars);

    public static readonly Preset[] All =
    [
        new("Obsidian", Hex(0x0C0E12), BackgroundStyle.Solid, 0.72f, Hex(0x13161C), 0.92f, Hex(0x5ED9C7), true, Hex(0x2A303C), 6f, BarStyle.Glass),
        new("Midnight", Hex(0x0A1024), BackgroundStyle.Gradient, 0.80f, Hex(0x111A35), 0.95f, Hex(0x7AA2FF), true, Hex(0x22305A), 8f, BarStyle.Glass),
        new("Ember", Hex(0x140C0A), BackgroundStyle.Glow, 0.80f, Hex(0x1E1210), 0.95f, Hex(0xF07D4F), true, Hex(0x3A2420), 6f, BarStyle.Gradient),
        new("Amethyst", Hex(0x120C1C), BackgroundStyle.Glass, 0.82f, Hex(0x1B1328), 0.95f, Hex(0xC39BFF), true, Hex(0x33264A), 7f, BarStyle.Glass),
        new("Minimal", Hex(0x000000), BackgroundStyle.Solid, 0.40f, Hex(0x000000), 0.35f, Hex(0xE6E8EC), false, Hex(0x2A303C), 2f, BarStyle.Flat),
    ];

    public static void Apply(Preset p, Configuration c)
    {
        c.BackgroundColor = p.Background;
        c.BackgroundStyle = p.Style;
        c.BackgroundOpacity = p.Opacity;
        c.HeaderColor = p.Header;
        c.HeaderOpacity = p.HeaderOpacity;
        c.WindowAccent = p.Accent;
        c.ShowBorder = p.Border;
        c.BorderColor = p.BorderColor;
        c.BorderThickness = 1f;
        c.WindowRounding = p.Rounding;
        c.ShowHeaderLine = true;
        c.ShowHeaderLogo = true;
        c.TextColor = Hex(0xE6E8EC);
        c.BarStyle = p.Bars;
    }

    private static Vector4 Hex(uint rgb)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
