using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Aethermeter.Models;

namespace Aethermeter.UI;

/// <summary>Paints one meter bar in the chosen style. Shared by the meter and the breakdown.</summary>
public static class Bars
{
    public static void Track(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, float alpha)
        => dl.AddRectFilled(min, max, Theme.U32(Theme.Surface, 0.55f * alpha), rounding);

    public static void Fill(ImDrawListPtr dl, Vector2 min, Vector2 max, float fraction, Vector4 color, BarStyle style, float rounding, float opacity, bool hovered)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction <= 0.001f) return;

        var end = new Vector2(min.X + (max.X - min.X) * fraction, max.Y);
        var c = hovered ? Theme.Lighten(color, 0.15f) : color;
        var h = max.Y - min.Y;

        switch (style)
        {
            case BarStyle.Flat:
                dl.AddRectFilled(min, end, Theme.U32(c, opacity), rounding);
                break;

            case BarStyle.Gradient:
                // Deep on the left, full colour at the leading edge: the bar reads as moving forward.
                dl.AddRectFilled(min, end, Theme.U32(Theme.Darken(c, 0.45f), opacity), rounding);
                dl.AddRectFilledMultiColor(min + new Vector2(rounding * 0.5f, 0), end,
                    Theme.U32(c, 0f), Theme.U32(c, opacity), Theme.U32(c, opacity), Theme.U32(c, 0f));
                break;

            case BarStyle.Glass:
                dl.AddRectFilled(min, end, Theme.U32(Theme.Darken(c, 0.25f), opacity), rounding);
                // Light falling from above, and a bright lip along the top edge.
                dl.AddRectFilledMultiColor(min, new Vector2(end.X, min.Y + h * 0.55f),
                    Theme.U32(Theme.Lighten(c, 0.35f), opacity * 0.55f), Theme.U32(Theme.Lighten(c, 0.35f), opacity * 0.55f),
                    Theme.U32(c, 0f), Theme.U32(c, 0f));
                dl.AddLine(new Vector2(min.X + rounding, min.Y + 0.5f), new Vector2(end.X - rounding, min.Y + 0.5f),
                    Theme.U32(Theme.Lighten(c, 0.6f), opacity * 0.7f), 1f);
                // Leading edge glint.
                dl.AddRectFilled(new Vector2(Math.Max(end.X - 2f, min.X), min.Y), end, Theme.U32(Theme.Lighten(c, 0.5f), opacity), rounding);
                break;

            case BarStyle.Outline:
                dl.AddRectFilled(min, end, Theme.U32(c, opacity * 0.22f), rounding);
                dl.AddRect(min, end, Theme.U32(c, opacity), rounding, ImDrawFlags.None, 1.2f);
                break;
        }
    }
}
