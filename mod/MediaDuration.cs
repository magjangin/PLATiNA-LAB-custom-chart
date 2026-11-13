using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace PLATiNALAB.Mod;

/// <summary>
/// music.ogg / video.mp4의 재생 길이(초)를 파일 헤더에서 읽는다.
/// 게임은 차트 endPosition(→ PatternData.DurationMicroSec)에서 곡을 끝내므로, 커스텀 곡 차트를 미디어 길이에 맞추는 데 쓴다.
/// </summary>
internal static class MediaDuration
{
    private const int MaxCache = 64;
    private static readonly ConcurrentDictionary<string, (long Ticks, double? Duration)> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ogg Vorbis: 마지막 페이지의 granule position / 식별 헤더의 샘플레이트. 대용량 파일 전체 로드 없이 스트림 앞/뒤만 읽는다.</summary>
    public static double? Ogg(string path) => Cached("ogg", path, ReadOgg);

    /// <summary>MP4: moov/mvhd 박스의 duration / timescale.</summary>
    public static double? Mp4(string path) => Cached("mp4", path, ReadMp4);

    private static double? Cached(string kind, string path, Func<string, double?> reader)
    {
        if (string.IsNullOrEmpty(path)) return null;
        long ticks = 0;
        try { ticks = File.GetLastWriteTimeUtc(path).Ticks; } catch { }
        var key = $"{kind}:{path}";
        if (ticks != 0 && s_cache.TryGetValue(key, out var cached) && cached.Ticks == ticks)
            return cached.Duration;

        var dur = reader(path);
        if (ticks != 0)
        {
            if (s_cache.Count >= MaxCache && !s_cache.ContainsKey(key)) s_cache.Clear();
            s_cache[key] = (ticks, dur);
        }
        return dur;
    }

    private static double? ReadOgg(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            long fileLen = fs.Length;
            if (fileLen < 64) return null;

            // 1. 앞부분(최대 8KB)에서 식별 헤더(\u0001vorbis) 및 샘플레이트 찾기
            int headSize = (int)Math.Min(8192, fileLen);
            var headBuf = new byte[headSize];
            ReadFully(fs, headBuf, 0, headSize);
            int vorbis = headBuf.AsSpan(0, headSize).IndexOf("\u0001vorbis"u8);
            if (vorbis < 0 || vorbis + 16 > headSize) return null;
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(headBuf.AsSpan(vorbis + 12));
            if (rate == 0) return null;

            // 2. 뒷부분(최대 64KB)에서 마지막 OggS 페이지를 역순 탐색
            int tailSize = (int)Math.Min(65536, fileLen);
            fs.Position = fileLen - tailSize;
            var tailBuf = new byte[tailSize];
            ReadFully(fs, tailBuf, 0, tailSize);

            var tail = tailBuf.AsSpan(0, tailSize);
            var oggS = "OggS"u8;
            for (int i = tailSize - 27; i >= 0; i--)
            {
                if (tail[i] != (byte)'O' || !tail.Slice(i, 4).SequenceEqual(oggS)) continue;
                long granule = BinaryPrimitives.ReadInt64LittleEndian(tail.Slice(i + 6));
                if (granule > 0) return granule / (double)rate;
            }
        }
        catch (Exception e) { Core.Log.Warning($"[MediaDuration] ogg 길이 읽기 실패: {e.Message}"); }
        return null;
    }

    private static double? ReadMp4(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var moov = FindBox(fs, 0, fs.Length, "moov"u8);
            if (moov == null) return null;
            var mvhd = FindBox(fs, moov.Value.start, moov.Value.end, "mvhd"u8);
            if (mvhd == null) return null;
            fs.Position = mvhd.Value.start;
            var buf = new byte[32];
            ReadFully(fs, buf, 0, buf.Length);
            if (buf[0] == 1) // version 1: 64비트 시간
            {
                uint scale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20));
                ulong dur = BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(24));
                return scale > 0 ? dur / (double)scale : null;
            }
            else
            {
                uint scale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12));
                uint dur = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(16));
                return scale > 0 ? dur / (double)scale : null;
            }
        }
        catch (Exception e) { Core.Log.Warning($"[MediaDuration] mp4 길이 읽기 실패: {e.Message}"); }
        return null;
    }

    /// <summary>[from, to) 범위에서 박스를 찾아 내용 범위(헤더 제외)를 돌려준다.</summary>
    private static (long start, long end)? FindBox(FileStream fs, long from, long to, ReadOnlySpan<byte> type)
    {
        var header = new byte[16];
        long pos = from;
        while (pos + 8 <= to)
        {
            fs.Position = pos;
            ReadFully(fs, header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            int headerSize = 8;
            if (size == 1)
            {
                ReadFully(fs, header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0) size = to - pos;
            if (size < headerSize) return null;
            if (header.AsSpan(4, 4).SequenceEqual(type)) return (pos + headerSize, pos + size);
            pos += size;
        }
        return null;
    }

    /// <summary>net6에는 Stream.ReadExactly가 없다.</summary>
    private static void ReadFully(Stream s, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int n = s.Read(buffer, offset, count);
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
            count -= n;
        }
    }
}
