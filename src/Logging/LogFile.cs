using System;
using System.IO;
using System.Text;

namespace Aethermeter.Logging;

/// <summary>
/// One log file being written. Opened shared for reading, so the Archon App can follow it live;
/// lines are buffered and flushed a couple of times a second.
/// </summary>
public sealed class LogFile : IDisposable
{
    private readonly StreamWriter writer;
    private long index;
    private DateTime lastFlush = DateTime.UtcNow;

    public string Path { get; }
    public long Lines => index;

    public LogFile(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // Reopening today's file appends to it: line numbers (and so hashes) carry on from its end.
        if (File.Exists(path))
        {
            using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(existing);
            while (reader.ReadLine() is not null) index++;
        }

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
    }

    public void Write(string body)
    {
        writer.Write(body);
        writer.Write(LogLine.Hash(body, ++index));
        writer.Write("\r\n");
    }

    public void FlushIfDue()
    {
        if ((DateTime.UtcNow - lastFlush).TotalMilliseconds < 400) return;
        lastFlush = DateTime.UtcNow;
        writer.Flush();
    }

    public void Dispose()
    {
        try
        {
            writer.Flush();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
