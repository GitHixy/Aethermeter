using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Aethermeter.Logging;

/// <summary>
/// Builds one line in the ACT network log format the FFLogs parser reads:
/// <c>type|timestamp|field|field|...|hash</c>. Field formats (hex or decimal, padding, decimals)
/// follow the FFXIV ACT plugin's own lines as documented in cactbot's LogGuide.
/// </summary>
public sealed class LogLine
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly StringBuilder sb = new(256);

    public int Type { get; }

    public LogLine(int type, DateTimeOffset time)
    {
        Type = type;
        sb.Append(type.ToString("00", Inv)).Append('|').Append(time.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", Inv)).Append('|');
    }

    public LogLine Str(string? s)
    {
        // A pipe inside a name would shift every field after it.
        if (!string.IsNullOrEmpty(s)) sb.Append(s.Replace('|', '/'));
        sb.Append('|');
        return this;
    }

    public LogLine Dec(long v) => Str(v.ToString(Inv));

    /// <summary>Upper-case hex without padding ("F67", "0").</summary>
    public LogLine Hex(ulong v) => Str(v.ToString("X", Inv));

    /// <summary>Upper-case hex, at least two digits ("00", "1E").</summary>
    public LogLine Hex2(ulong v) => Str(v.ToString("X2", Inv));

    /// <summary>An entity id: eight hex digits.</summary>
    public LogLine Id(uint v) => Str(v.ToString("X8", Inv));

    public LogLine F2(float v) => Str(v.ToString("0.00", Inv));
    public LogLine F3(float v) => Str(v.ToString("0.000", Inv));

    /// <summary>A position as ACT writes it: x, then the game's z, then height, then heading.</summary>
    public LogLine Pos(Vector3 p, float heading) => F2(p.X).F2(p.Z).F2(p.Y).F2(heading);

    public LogLine Blank(int count = 1)
    {
        for (var i = 0; i < count; i++) sb.Append('|');
        return this;
    }

    /// <summary>The line up to (and including) its last separator, without the hash.</summary>
    public string Body => sb.ToString();

    /// <summary>
    /// The trailing hash. ACT's exact recipe isn't published; this is a stable SHA-256 of the line and
    /// its number in the file, in the same 32-hex-digit shape. Readers use it as an opaque checksum.
    /// </summary>
    public static string Hash(string body, long index)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(body + index.ToString(Inv)), digest);
        return Convert.ToHexString(digest[..16]).ToLowerInvariant();
    }

    /// <summary>Line type of a stored body, read back from its prefix.</summary>
    public static int TypeOf(string body)
    {
        var bar = body.IndexOf('|');
        return bar > 0 && int.TryParse(body.AsSpan(0, bar), NumberStyles.Integer, Inv, out var t) ? t : -1;
    }
}
