using System.Text;

namespace FractalMcp.Protocol;

public static class FractalProtocol
{
    public const byte GetSetBypass = 0x0A;
    public const byte GetSetChannel = 0x0B;
    public const byte GetSetScene = 0x0C;
    public const byte QueryPresetName = 0x0D;
    public const byte QuerySceneName = 0x0E;
    public const byte GetSetLooper = 0x0F;
    public const byte TapTempo = 0x10;
    public const byte Tuner = 0x11;
    public const byte StatusDump = 0x13;
    public const byte GetSetTempo = 0x14;

    public static byte[] Frame(FractalDeviceModel model, byte opcode, params byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Any(value => value > 0x7F))
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "SysEx payload bytes must be 7-bit values.");
        }

        byte[] frame = new byte[payload.Length + 8];
        byte[] header = [0xF0, 0x00, 0x01, 0x74, model.ModelId(), opcode];
        header.CopyTo(frame, 0);
        payload.CopyTo(frame, 6);
        frame[^2] = ComputeChecksum(frame.AsSpan(0, frame.Length - 2));
        frame[^1] = 0xF7;
        return frame;
    }

    public static byte ComputeChecksum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0;
        foreach (byte value in bytes)
        {
            checksum ^= value;
        }

        return (byte)(checksum & 0x7F);
    }

    public static bool IsValidFrame(ReadOnlySpan<byte> frame, FractalDeviceModel model, byte opcode)
    {
        if (frame.Length < 8 || frame[0] != 0xF0 || frame[^1] != 0xF7 ||
            frame[1] != 0x00 || frame[2] != 0x01 || frame[3] != 0x74 ||
            frame[4] != model.ModelId() || frame[5] != opcode)
        {
            return false;
        }

        for (int index = 1; index < frame.Length - 1; index++)
        {
            if (frame[index] > 0x7F)
            {
                return false;
            }
        }

        return frame[^2] == ComputeChecksum(frame[..^2]);
    }

    public static byte[] BuildBypass(FractalDeviceModel model, int effectId, bool? bypassed = null) =>
        Frame(model, GetSetBypass, Low7(effectId), High7(effectId), bypassed.HasValue ? (byte)(bypassed.Value ? 1 : 0) : (byte)0x7F);

    public static byte[] BuildChannel(FractalDeviceModel model, int effectId, int? channel = null)
    {
        if (channel is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "Block channel must be 1-4.");
        }

        return Frame(model, GetSetChannel, Low7(effectId), High7(effectId), channel.HasValue ? (byte)(channel.Value - 1) : (byte)0x7F);
    }

    public static byte[] BuildScene(FractalDeviceModel model, int? scene = null)
    {
        if (scene is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(scene), "Scene must be 1-8.");
        }

        return Frame(model, GetSetScene, scene.HasValue ? (byte)(scene.Value - 1) : (byte)0x7F);
    }

    public static byte[] BuildPresetNameQuery(FractalDeviceModel model, int? preset = null)
    {
        if (preset is < 0 or > 511)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), "Preset must be 0-511.");
        }

        return preset.HasValue
            ? Frame(model, QueryPresetName, Low7(preset.Value), High7(preset.Value))
            : Frame(model, QueryPresetName, 0x7F, 0x7F);
    }

    public static byte[] BuildSceneNameQuery(FractalDeviceModel model, int? scene = null)
    {
        if (scene is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(scene), "Scene must be 1-8.");
        }

        return Frame(model, QuerySceneName, scene.HasValue ? (byte)(scene.Value - 1) : (byte)0x7F);
    }

    public static byte[] BuildTempo(FractalDeviceModel model, int? bpm = null)
    {
        if (bpm is < 20 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(bpm), "Tempo must be 20-250 BPM.");
        }

        return bpm.HasValue
            ? Frame(model, GetSetTempo, Low7(bpm.Value), High7(bpm.Value))
            : Frame(model, GetSetTempo, 0x7F, 0x7F);
    }

    public static byte[] BuildLooper(FractalDeviceModel model, int? action = null)
    {
        if (action is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(action), "Looper action must be 0-5.");
        }

        return Frame(model, GetSetLooper, action.HasValue ? (byte)action.Value : (byte)0x7F);
    }

    public static byte[] BuildTapTempo(FractalDeviceModel model) => Frame(model, TapTempo);
    public static byte[] BuildTuner(FractalDeviceModel model, bool enabled) => Frame(model, Tuner, enabled ? (byte)1 : (byte)0);
    public static byte[] BuildStatusDump(FractalDeviceModel model) => Frame(model, StatusDump);

    public static (int EffectId, bool Bypassed) ParseBypass(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, GetSetBypass, 11);
        return (Read14(frame[6], frame[7]), frame[8] switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Invalid bypass state in device response."),
        });
    }

    public static (int EffectId, int Channel) ParseChannel(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, GetSetChannel, 11);
        if (frame[8] > 3)
        {
            throw new InvalidDataException("Invalid block channel in device response.");
        }

        return (Read14(frame[6], frame[7]), frame[8] + 1);
    }

    public static int ParseScene(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, GetSetScene, 9);
        if (frame[6] > 7)
        {
            throw new InvalidDataException("Invalid scene in device response.");
        }

        return frame[6] + 1;
    }

    public static (int Preset, string Name) ParsePresetName(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, QueryPresetName, 42);
        int preset = Read14(frame[6], frame[7]);
        if (preset > 511)
        {
            throw new InvalidDataException("Invalid preset number in device response.");
        }

        return (preset, ReadName(frame.Slice(8, 32)));
    }

    public static (int Scene, string Name) ParseSceneName(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, QuerySceneName, 41);
        if (frame[6] > 7)
        {
            throw new InvalidDataException("Invalid scene number in device response.");
        }

        return (frame[6] + 1, ReadName(frame.Slice(7, 32)));
    }

    public static int ParseTempo(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, GetSetTempo, 10);
        return Read14(frame[6], frame[7]);
    }

    public static LooperState ParseLooper(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        Require(frame, model, GetSetLooper, 9);
        byte state = frame[6];
        return new LooperState(
            Record: (state & 0x01) != 0,
            Play: (state & 0x02) != 0,
            Overdub: (state & 0x04) != 0,
            Once: (state & 0x08) != 0,
            Reverse: (state & 0x10) != 0,
            HalfSpeed: (state & 0x20) != 0);
    }

    public static IReadOnlyList<EffectStatus> ParseStatusDump(ReadOnlySpan<byte> frame, FractalDeviceModel model)
    {
        if (!IsValidFrame(frame, model, StatusDump) || (frame.Length - 8) % 3 != 0)
        {
            throw new InvalidDataException("Invalid status dump response.");
        }

        var result = new List<EffectStatus>((frame.Length - 8) / 3);
        for (int offset = 6; offset < frame.Length - 2; offset += 3)
        {
            byte state = frame[offset + 2];
            result.Add(new EffectStatus(
                EffectId: Read14(frame[offset], frame[offset + 1]),
                Bypassed: (state & 0x01) != 0,
                Channel: ((state >> 1) & 0x07) + 1,
                ChannelCount: (state >> 4) & 0x07));
        }

        return result;
    }

    private static byte Low7(int value)
    {
        Require14(value);
        return (byte)(value & 0x7F);
    }

    private static byte High7(int value)
    {
        Require14(value);
        return (byte)((value >> 7) & 0x7F);
    }

    private static void Require14(int value)
    {
        if (value is < 0 or > 0x3FFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must fit in 14 MIDI data bits.");
        }
    }

    private static int Read14(byte low, byte high) => low | (high << 7);

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        int nullIndex = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(nullIndex < 0 ? bytes : bytes[..nullIndex]).TrimEnd(' ');
    }

    private static void Require(ReadOnlySpan<byte> frame, FractalDeviceModel model, byte opcode, int length)
    {
        if (frame.Length != length || !IsValidFrame(frame, model, opcode))
        {
            throw new InvalidDataException($"Invalid Fractal SysEx response for opcode 0x{opcode:X2}.");
        }
    }
}

public sealed record EffectStatus(int EffectId, bool Bypassed, int Channel, int ChannelCount);
public sealed record LooperState(bool Record, bool Play, bool Overdub, bool Once, bool Reverse, bool HalfSpeed);
