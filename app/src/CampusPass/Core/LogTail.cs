using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CampusPass.Core
{
    /// <summary>
    /// The log is append-only and unbounded. The legacy viewer called
    /// File.ReadAllText on every refresh, which on a months-old multi-megabyte
    /// log allocates the whole file into the UI process. Reading backwards in
    /// fixed blocks keeps the cost proportional to the lines shown.
    /// </summary>
    static class LogTail
    {
        const int BlockSize = 64 * 1024;

        internal static string[] ReadLastLines(string path, int maxLines)
        {
            if (maxLines <= 0) return Array.Empty<string>();
            if (!File.Exists(path)) return new[] { "暂无日志" };

            var tail = new List<byte>();
            int newlines = 0;
            long pos;

            // FileShare.ReadWrite: the background service holds this open for append
            // most of the time, and a read lock would throw on every refresh.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                pos = stream.Length;
                while (pos > 0 && newlines <= maxLines)
                {
                    int readSize = (int)Math.Min(BlockSize, pos);
                    pos -= readSize;
                    stream.Seek(pos, SeekOrigin.Begin);
                    byte[] block = new byte[readSize];
                    int got = ReadFull(stream, block, readSize);

                    tail.InsertRange(0, SubArray(block, 0, got));
                    for (int i = 0; i < got; i++) if (block[i] == (byte)'\n') newlines++;
                    if (got < readSize) break;
                }
            }

            if (tail.Count == 0) return new[] { "暂无日志" };

            // AppData.Log uses AppendAllText(Encoding.UTF8), which emits a BOM when
            // it creates the file. File.ReadAllText stripped it silently; decoding by
            // hand does not, so the first line would render with a stray .
            if (pos == 0) StripByteOrderMark(tail);

            string text = Encoding.UTF8.GetString(tail.ToArray());
            var lines = new List<string>(text.Split('\n'));

            // A leading fragment only exists when we stopped before the start of file.
            if (pos > 0 && lines.Count > 0) lines.RemoveAt(0);
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].EndsWith("\r")) lines[i] = lines[i].Substring(0, lines[i].Length - 1);

            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > maxLines) lines.RemoveRange(0, lines.Count - maxLines);
            return lines.Count == 0 ? new[] { "暂无日志" } : lines.ToArray();
        }

        internal static long GetSizeBytes(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        static void StripByteOrderMark(List<byte> tail)
        {
            if (tail.Count >= 3 && tail[0] == 0xEF && tail[1] == 0xBB && tail[2] == 0xBF)
                tail.RemoveRange(0, 3);
        }

        static int ReadFull(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        static byte[] SubArray(byte[] source, int start, int length)
        {
            if (length == source.Length) return source;
            var result = new byte[length];
            Array.Copy(source, start, result, 0, length);
            return result;
        }
    }
}
