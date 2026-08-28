using System.Text;

namespace TickLab.Desktop.Core.Recording;

/// <summary>
/// Dependency-free AVI 1.0 MJPEG writer used by the TickLab recorder.
/// Frames are independent JPEG keyframes. The writer emits a legacy idx1 index
/// plus the standard AVI timing/buffer metadata expected by Windows/VLC players.
/// </summary>
internal sealed class MjpegAviWriter : IDisposable
{
    private const int AviHasIndex = 0x10;
    private const int AviIsInterleaved = 0x100;
    private const int AviTrustChunkType = 0x800;

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly int _framesPerSecond;
    private readonly List<FrameIndexEntry> _index = new();

    private long _riffSizePosition;
    private long _hdrlSizePosition;
    private long _strlSizePosition;
    private long _moviSizePosition;
    private long _moviDataStart;
    private long _avihMaxBytesPerSecondPosition;
    private long _avihTotalFramesPosition;
    private long _avihSuggestedBufferPosition;
    private long _strhLengthPosition;
    private long _strhSuggestedBufferPosition;
    private long _totalJpegBytes;
    private int _largestFrameSize;
    private bool _finished;

    public MjpegAviWriter(string path, int width, int height, int framesPerSecond)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));

        _width = width;
        _height = height;
        _framesPerSecond = framesPerSecond;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        _stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.SequentialScan);
        _writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader();
    }

    public int FrameCount => _index.Count;
    public long Length => _stream.Length;

    public void WriteJpegFrame(ReadOnlySpan<byte> jpeg)
    {
        if (_finished)
            throw new InvalidOperationException("AVI writer has already been finalized.");
        if (jpeg.Length == 0)
            return;

        long chunkStart = _stream.Position;
        WriteFourCc("00dc");
        _writer.Write(jpeg.Length);
        _writer.Write(jpeg);
        if ((jpeg.Length & 1) != 0)
            _writer.Write((byte)0);

        int offset = checked((int)(chunkStart - _moviDataStart));
        _index.Add(new FrameIndexEntry(offset, jpeg.Length));
        _totalJpegBytes += jpeg.Length;
        if (jpeg.Length > _largestFrameSize)
            _largestFrameSize = jpeg.Length;
    }

    public void Finish()
    {
        if (_finished)
            return;
        _finished = true;

        long indexStart = _stream.Position;
        PatchInt32(_moviSizePosition, checked((int)(indexStart - (_moviSizePosition + 4))));

        WriteFourCc("idx1");
        _writer.Write(checked(_index.Count * 16));
        foreach (FrameIndexEntry entry in _index)
        {
            WriteFourCc("00dc");
            _writer.Write(0x10); // AVIIF_KEYFRAME
            _writer.Write(entry.Offset);
            _writer.Write(entry.Size);
        }

        long end = _stream.Position;
        int suggestedBuffer = Math.Max(64 * 1024, _largestFrameSize + 8);
        int maxBytesPerSecond = 0;
        if (_index.Count > 0)
        {
            long average = (_totalJpegBytes * _framesPerSecond + _index.Count - 1L) / _index.Count;
            // Give players headroom beyond the measured average rather than leaving
            // dwMaxBytesPerSec at zero, which causes some Windows readers to scan.
            maxBytesPerSecond = (int)Math.Min(int.MaxValue, Math.Max(1L, average + average / 4L));
        }

        PatchInt32(_avihMaxBytesPerSecondPosition, maxBytesPerSecond);
        PatchInt32(_avihTotalFramesPosition, _index.Count);
        PatchInt32(_avihSuggestedBufferPosition, suggestedBuffer);
        PatchInt32(_strhLengthPosition, _index.Count);
        PatchInt32(_strhSuggestedBufferPosition, suggestedBuffer);
        PatchInt32(_riffSizePosition, checked((int)(end - 8)));
        _writer.Flush();
        _stream.Flush();
    }

    public void Dispose()
    {
        try
        {
            Finish();
        }
        finally
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }

    private void WriteHeader()
    {
        WriteFourCc("RIFF");
        _riffSizePosition = _stream.Position;
        _writer.Write(0);
        WriteFourCc("AVI ");

        WriteFourCc("LIST");
        _hdrlSizePosition = _stream.Position;
        _writer.Write(0);
        WriteFourCc("hdrl");

        WriteFourCc("avih");
        _writer.Write(56);
        _writer.Write((int)Math.Round(1_000_000.0 / _framesPerSecond));
        _avihMaxBytesPerSecondPosition = _stream.Position;
        _writer.Write(0); // patched at Finish
        _writer.Write(0); // padding granularity
        _writer.Write(AviHasIndex | AviIsInterleaved | AviTrustChunkType);
        _avihTotalFramesPosition = _stream.Position;
        _writer.Write(0);
        _writer.Write(0); // initial frames
        _writer.Write(1); // streams
        _avihSuggestedBufferPosition = _stream.Position;
        _writer.Write(0); // patched at Finish
        _writer.Write(_width);
        _writer.Write(_height);
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0);

        WriteFourCc("LIST");
        _strlSizePosition = _stream.Position;
        _writer.Write(0);
        WriteFourCc("strl");

        WriteFourCc("strh");
        _writer.Write(56);
        WriteFourCc("vids");
        WriteFourCc("MJPG");
        _writer.Write(0); // flags
        _writer.Write((short)0); // priority
        _writer.Write((short)0); // language
        _writer.Write(0); // initial frames
        _writer.Write(1); // scale
        _writer.Write(_framesPerSecond); // rate
        _writer.Write(0); // start
        _strhLengthPosition = _stream.Position;
        _writer.Write(0); // length
        _strhSuggestedBufferPosition = _stream.Position;
        _writer.Write(0); // suggested buffer size patched at Finish
        _writer.Write(-1); // default quality
        _writer.Write(0); // sample size
        _writer.Write((short)0);
        _writer.Write((short)0);
        _writer.Write((short)Math.Min(short.MaxValue, _width));
        _writer.Write((short)Math.Min(short.MaxValue, _height));

        WriteFourCc("strf");
        _writer.Write(40);
        _writer.Write(40); // BITMAPINFOHEADER size
        _writer.Write(_width);
        _writer.Write(_height);
        _writer.Write((short)1); // planes
        _writer.Write((short)24); // bit count
        WriteFourCc("MJPG");
        _writer.Write(checked(_width * _height * 3));
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0);

        long endStrl = _stream.Position;
        PatchInt32(_strlSizePosition, checked((int)(endStrl - (_strlSizePosition + 4))));
        long endHdrl = _stream.Position;
        PatchInt32(_hdrlSizePosition, checked((int)(endHdrl - (_hdrlSizePosition + 4))));

        WriteFourCc("LIST");
        _moviSizePosition = _stream.Position;
        _writer.Write(0);
        _moviDataStart = _stream.Position;
        WriteFourCc("movi");
    }

    private void PatchInt32(long position, int value)
    {
        long current = _stream.Position;
        _stream.Position = position;
        _writer.Write(value);
        _stream.Position = current;
    }

    private void WriteFourCc(string value)
    {
        if (value.Length != 4)
            throw new ArgumentException("FOURCC must contain exactly four characters.", nameof(value));
        _writer.Write(Encoding.ASCII.GetBytes(value));
    }

    private readonly record struct FrameIndexEntry(int Offset, int Size);
}
