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

    /// <summary>
    /// 음원 길이. RIFF 헤더면 WAV, 아니면 Ogg Vorbis로 읽는다 (CustomMedia.CreateAudioClip과 같은 기준).
    /// 읽지 못하면 null — 호출부가 경고해야 곡이 기반 곡 길이에서 조용히 끝나지 않는다.
    /// </summary>
    public static double? Audio(string path) => Cached("audio", path, ReadAudio);

    private static double? ReadAudio(string path)
    {
        try
        {
            Span<byte> magic = stackalloc byte[4];
            using (var fs = File.OpenRead(path))
                if (fs.Read(magic) == 4 && magic.SequenceEqual("RIFF"u8)) return ReadWav(path);
        }
        catch (Exception e) { Core.Log.Warning($"[MediaDuration] 음원 형식 확인 실패: {e.Message}"); return null; }
        return ReadOgg(path);
    }

    /// <summary>WAV: fmt 청크의 byteRate와 data 청크 크기. data 크기가 0이거나 0xFFFFFFFF(스트리밍 기록)면 파일 끝까지로 본다.</summary>
    private static double? ReadWav(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            long len = fs.Length;
            if (len < 44) return null;
            var hdr = new byte[12];
            ReadFully(fs, hdr, 0, 12);
            if (!hdr.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return null;

            uint byteRate = 0;
            long dataLen = 0;
            var chunk = new byte[8];
            long pos = 12;
            while (pos + 8 <= len)
            {
                fs.Position = pos;
                ReadFully(fs, chunk, 0, 8);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
                long body = pos + 8;
                if (chunk.AsSpan(0, 4).SequenceEqual("fmt "u8) && size >= 16 && body + 16 <= len)
                {
                    var fmt = new byte[16];
                    ReadFully(fs, fmt, 0, 16);
                    byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(8));
                }
                else if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
                {
                    dataLen = size == 0 || size == uint.MaxValue || body + size > len ? len - body : size;
                }
                if (byteRate > 0 && dataLen > 0) return dataLen / (double)byteRate;
                pos = body + size + (size & 1); // 청크는 짝수 바이트로 정렬된다
            }
        }
        catch (Exception e) { Core.Log.Warning($"[MediaDuration] wav 길이 읽기 실패: {e.Message}"); }
        return null;
    }

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
            // 첫 페이지의 비트스트림 일련번호. 끝부분 역탐색에서 오디오 데이터 속 우연한 "OggS" 바이트나
            // 다른 논리 스트림의 페이지를 마지막 페이지로 오인하지 않도록 같은 일련번호만 받는다.
            uint? serial = headSize >= 27 && headBuf.AsSpan(0, 4).SequenceEqual("OggS"u8)
                ? BinaryPrimitives.ReadUInt32LittleEndian(headBuf.AsSpan(14))
                : null;

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
                // 페이지 헤더 검증: 버전 0, 세그먼트 테이블이 버퍼 안에 있음, 같은 논리 스트림
                if (tail[i + 4] != 0 || i + 27 + tail[i + 26] > tailSize) continue;
                if (serial != null && BinaryPrimitives.ReadUInt32LittleEndian(tail.Slice(i + 14)) != serial.Value) continue;
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
            long bodyLen = mvhd.Value.end - mvhd.Value.start;
            if (bodyLen < 20) return null;
            fs.Position = mvhd.Value.start;
            int toRead = (int)Math.Min(32, bodyLen);
            var buf = new byte[toRead];
            ReadFully(fs, buf, 0, toRead);
            if (buf[0] == 1) // version 1: 64비트 시간
            {
                if (toRead < 32) return null;
                uint scale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20));
                ulong dur = BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(24));
                // ISO/IEC 14496-12: 모든 비트가 1(ulong.MaxValue)이면 "길이 미정(fragmented/streamed)"
                if (scale == 0 || dur == 0 || dur == ulong.MaxValue) return null;
                return dur / (double)scale;
            }
            if (buf[0] == 0) // version 0: 32비트 시간
            {
                uint scale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12));
                uint dur = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(16));
                if (scale == 0 || dur == 0 || dur == uint.MaxValue) return null;
                return dur / (double)scale;
            }
            return null;
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
                if (pos + 16 > to) return null;
                ReadFully(fs, header, 8, 8);
                ulong wide = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                if (wide > (ulong)(to - pos)) return null;
                size = (long)wide;
                headerSize = 16;
            }
            else if (size == 0) size = to - pos;
            if (size < headerSize || size > to - pos) return null;
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
