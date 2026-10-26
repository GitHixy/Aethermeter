using System.Collections.Generic;
using System.Numerics;
using Aethermeter.Models;
using Aethermeter.Services;

namespace Aethermeter.UI;

/// <summary>
/// Bar colours. Jobs use the colours players already know from community tools; a job this
/// table doesn't list yet (a new one after an expansion) falls back on its role's colour.
/// </summary>
public static class JobColors
{
    private static readonly Dictionary<uint, Vector4> Jobs = new()
    {
        [1] = Hex(0xA8D2E6), [19] = Hex(0xA8D2E6), // GLA / PLD
        [3] = Hex(0xCF2621), [21] = Hex(0xCF2621), // MRD / WAR
        [32] = Hex(0xD126CC),                      // DRK
        [37] = Hex(0x796D30),                      // GNB
        [6] = Hex(0xFFF0DC), [24] = Hex(0xFFF0DC), // CNJ / WHM
        [28] = Hex(0x8657FF),                      // SCH
        [33] = Hex(0xFFE74A),                      // AST
        [40] = Hex(0x80A0F0),                      // SGE
        [2] = Hex(0xD69C00), [20] = Hex(0xD69C00), // PGL / MNK
        [4] = Hex(0x4164CD), [22] = Hex(0x4164CD), // LNC / DRG
        [29] = Hex(0xAF1964), [30] = Hex(0xAF1964), // ROG / NIN
        [34] = Hex(0xE46D04),                      // SAM
        [39] = Hex(0x965A90),                      // RPR
        [41] = Hex(0x108210),                      // VPR
        [5] = Hex(0x91BA5E), [23] = Hex(0x91BA5E), // ARC / BRD
        [31] = Hex(0x6EE1D6),                      // MCH
        [38] = Hex(0xE2B0AF),                      // DNC
        [7] = Hex(0xA579D6), [25] = Hex(0xA579D6), // THM / BLM
        [26] = Hex(0x2D9B78), [27] = Hex(0x2D9B78), // ACN / SMN
        [35] = Hex(0xE87B7B),                      // RDM
        [42] = Hex(0xFC92E1),                      // PCT
        [36] = Hex(0x4A6BFF),                      // BLU
    };

    public static readonly Vector4 Tank = Hex(0x3D6CC8);
    public static readonly Vector4 Healer = Hex(0x3FA34D);
    public static readonly Vector4 Dps = Hex(0xB8433E);
    public static readonly Vector4 Neutral = Hex(0x7A8293);

    /// <summary>Chocobo yellow, for companions.</summary>
    public static readonly Vector4 Chocobo = Hex(0xF2C94C);

    public static Vector4 For(Combat.Combatant c, Configuration config)
        => c.IsChocobo && config.BarColorMode != BarColorMode.Accent ? Chocobo : For(c.JobId, config);

    public static Vector4 For(uint jobId, Configuration config)
    {
        switch (config.BarColorMode)
        {
            case BarColorMode.Accent:
                return config.AccentColor;
            case BarColorMode.Job when Jobs.TryGetValue(jobId, out var c):
                return c;
            default:
                return GameData.JobInfo(jobId).Role switch
                {
                    GameData.Role.Tank => Tank,
                    GameData.Role.Healer => Healer,
                    GameData.Role.None => Neutral,
                    _ => Dps,
                };
        }
    }

    private static Vector4 Hex(uint rgb)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
