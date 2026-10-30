using System.Buffers.Binary;
using System.Text;

namespace PLATiNALAB.Mod;

/// <summary>
/// music.ogg / video.mp4의 재생 길이(초)를 파일 헤더에서 읽는다.
/// 게임은 차트 endPosition(→ PatternData.DurationMicroSec)에서 곡을 끝내므로, 커스텀 곡 차트를 미디어 길이에 맞추는 데 쓴다.
/// </summary>
internal static class MediaDuration
{
    /// <summary>Ogg Vorbis: 마지막 페이지의 granule position / 식별 헤더의 샘플레이트.</summary>
    public static double? Ogg(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var vorbis = IndexOf(data, Encoding.ASCII.GetBytes("\u0001vorbis"), 0);
            if (vorbis < 0) return null;
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(vorbis + 12));
            var oggS = Encoding.ASCII.GetBytes("OggS");
            for (int i = data.Length - 27; i >= 0; i--)
            {
                if (data[i] != 'O' || !data.AsSpan(i, 4).SequenceEqual(oggS)) continue;
                long granule = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + 6));
                return rate > 0 && granule > 0 ? granule / (double)rate : null;
            }
        }
        catch (Exception e) { Core.Log.Warning($"[MediaDuration] ogg 길이 읽기 실패: {e.Message}"); }
        return null;
    }

    /// <summary>MP4: moov/mvhd 박스의 duration / timescale.</summary>
    public static double? Mp4(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var moov = FindBox(fs, 0, fs.Length, "moov");
            if (moov == null) return null;
            var mvhd = FindBox(fs, moov.Value.start, moov.Value.end, "mvhd");
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
    private static (long start, long end)? FindBox(FileStream fs, long from, long to, string type)
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
            if (Encoding.ASCII.GetString(header, 4, 4) == type) return (pos + headerSize, pos + size);
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

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        for (int i = start; i <= data.Length - pattern.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) return i;
        return -1;
    }
}
