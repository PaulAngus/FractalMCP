namespace FractalMcp.Protocol;

/// <summary>Reassembles split MIDI callbacks and ignores interleaved real-time bytes.</summary>
public sealed class SysexAssembler
{
    private const int MaximumFrameLength = 64 * 1024;
    private readonly List<byte> _buffer = [];

    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<byte[]>();
        foreach (byte value in bytes)
        {
            if (value >= 0xF8)
            {
                continue;
            }

            if (value == 0xF0)
            {
                _buffer.Clear();
                _buffer.Add(value);
            }
            else if (_buffer.Count > 0)
            {
                if (value == 0xF7)
                {
                    _buffer.Add(value);
                    frames.Add(_buffer.ToArray());
                    _buffer.Clear();
                }
                else if (value >= 0x80 || _buffer.Count >= MaximumFrameLength)
                {
                    _buffer.Clear();
                }
                else
                {
                    _buffer.Add(value);
                }
            }
        }

        return frames;
    }
}

