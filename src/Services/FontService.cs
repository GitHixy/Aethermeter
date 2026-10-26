using System;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Aethermeter.Models;

namespace Aethermeter.Services;

/// <summary>
/// Every face the meter draws with. Bundled OFL faces carry Latin only, to keep the atlas small;
/// the interface face carries the client language's full set, so names in any script render.
/// Fonts are locked once per frame around drawing and drawn at any size through the draw list.
/// </summary>
public sealed class FontService : IDisposable
{
    /// <summary>Meter text sits around 12-24 px; baking a little above that keeps it crisp when scaled.</summary>
    private const float BakeSizePx = 30f;

    private static readonly ushort[] LatinRanges =
    [
        0x0020, 0x007E,
        0x00A0, 0x017F,
        0x2013, 0x2026,
        0,
    ];

    private static readonly int Count = Enum.GetValues<FontId>().Length;

    private readonly IFontHandle?[] handles = new IFontHandle?[Count];
    private readonly ILockedImFont?[] locked = new ILockedImFont?[Count];
    private readonly ImFontPtr[] frameFonts = new ImFontPtr[Count];
    private bool inFrame;

    public FontService()
    {
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        var dir = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Assets", "Fonts");

        foreach (var id in Enum.GetValues<FontId>())
        {
            try
            {
                handles[(int)id] = id switch
                {
                    FontId.GameTrumpGothic => atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.TrumpGothic, BakeSizePx)),
                    FontId.GameJupiter => atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, BakeSizePx)),
                    FontId.Interface => atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
                    {
                        var font = tk.AddDalamudDefaultFont(BakeSizePx);
                        tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = BakeSizePx, MergeFont = font });
                    })),
                    _ => Bundled(atlas, Path.Combine(dir, FileName(id))),
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not create font handle for {Font}", id);
            }
        }
    }

    private static IFontHandle Bundled(IFontAtlas atlas, string path)
        => atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            if (!File.Exists(path))
            {
                Plugin.Log.Warning("Font file missing: {Path}", path);
                tk.AddDalamudDefaultFont(BakeSizePx);
                return;
            }

            tk.AddFontFromFile(path, new SafeFontConfig { SizePx = BakeSizePx, GlyphRanges = LatinRanges });
        }));

    private static string FileName(FontId id) => id switch
    {
        FontId.BarlowCondensed => "BarlowCondensed-Bold.ttf",
        FontId.Barlow => "Barlow-SemiBold.ttf",
        FontId.BebasNeue => "BebasNeue-Regular.ttf",
        _ => string.Empty,
    };

    public static string DisplayName(FontId id) => id switch
    {
        FontId.Interface => "Interface (all languages)",
        FontId.GameTrumpGothic => "Game condensed (Trump Gothic)",
        FontId.GameJupiter => "Game serif (Jupiter)",
        FontId.BarlowCondensed => "Barlow Condensed",
        FontId.Barlow => "Barlow",
        FontId.BebasNeue => "Bebas Neue",
        _ => id.ToString(),
    };

    /// <summary>Faces that can't draw Japanese, Chinese or Korean names.</summary>
    public static bool IsLatinOnly(FontId id) => id is not FontId.Interface;

    public void BeginFrame()
    {
        if (inFrame) return;
        inFrame = true;

        var fallback = ImGui.GetFont();
        for (var i = 0; i < handles.Length; i++)
        {
            frameFonts[i] = fallback;
            var handle = handles[i];
            if (handle is null || !handle.Available) continue;

            try
            {
                locked[i] = handle.Lock();
                frameFonts[i] = locked[i]!.ImFont;
            }
            catch (Exception)
            {
                locked[i] = null;
            }
        }
    }

    public void EndFrame()
    {
        if (!inFrame) return;
        inFrame = false;

        for (var i = 0; i < locked.Length; i++)
        {
            locked[i]?.Dispose();
            locked[i] = null;
        }
    }

    /// <summary>The font to draw with this frame. Only valid between BeginFrame and EndFrame.</summary>
    public ImFontPtr Get(FontId id) => frameFonts[(int)id];

    public void Dispose()
    {
        EndFrame();
        foreach (var handle in handles)
            handle?.Dispose();
    }
}
