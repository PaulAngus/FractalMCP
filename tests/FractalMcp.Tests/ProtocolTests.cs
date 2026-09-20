using System.Text;
using FractalMcp.Protocol;

namespace FractalMcp.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData(FractalDeviceModel.AxeFxIII, 0x10)]
    [InlineData(FractalDeviceModel.FM3, 0x11)]
    [InlineData(FractalDeviceModel.FM9, 0x12)]
    public void FramesUseTheSelectedModelByte(FractalDeviceModel model, byte expected)
    {
        Assert.Equal(expected, FractalProtocol.BuildScene(model)[4]);
    }

    [Theory]
    [InlineData(0, "F0 00 01 74 12 0D 00 00 1A F7")]
    [InlineData(127, "F0 00 01 74 12 0D 7F 00 65 F7")]
    [InlineData(128, "F0 00 01 74 12 0D 00 01 1B F7")]
    [InlineData(511, "F0 00 01 74 12 0D 7F 03 66 F7")]
    public void Fm9PresetNameQueriesMatchKnownFrames(int preset, string expected)
    {
        Assert.Equal(expected, Hex(FractalProtocol.BuildPresetNameQuery(FractalDeviceModel.FM9, preset)));
    }

    [Theory]
    [InlineData(1, "F0 00 01 74 12 0E 00 19 F7")]
    [InlineData(8, "F0 00 01 74 12 0E 07 1E F7")]
    public void SceneNameQueriesUseOneBasedPublicValues(int scene, string expected)
    {
        Assert.Equal(expected, Hex(FractalProtocol.BuildSceneNameQuery(FractalDeviceModel.FM9, scene)));
    }

    [Fact]
    public void ParsesPresetAndSceneNames()
    {
        byte[] preset = NameResponse(FractalDeviceModel.FM3, FractalProtocol.QueryPresetName, [0, 1], "Clean");
        byte[] scene = NameResponse(FractalDeviceModel.FM3, FractalProtocol.QuerySceneName, [2], "Lead");

        Assert.Equal((128, "Clean"), FractalProtocol.ParsePresetName(preset, FractalDeviceModel.FM3));
        Assert.Equal((3, "Lead"), FractalProtocol.ParseSceneName(scene, FractalDeviceModel.FM3));
    }

    [Fact]
    public void RejectsWrongModelAndBadChecksum()
    {
        byte[] frame = FractalProtocol.BuildScene(FractalDeviceModel.FM9, 1);
        Assert.False(FractalProtocol.IsValidFrame(frame, FractalDeviceModel.FM3, FractalProtocol.GetSetScene));
        frame[^2] ^= 1;
        Assert.False(FractalProtocol.IsValidFrame(frame, FractalDeviceModel.FM9, FractalProtocol.GetSetScene));
    }

    [Fact]
    public void StatusDumpDecodesPackets()
    {
        byte[] frame = FractalProtocol.Frame(FractalDeviceModel.AxeFxIII, FractalProtocol.StatusDump,
            0x23, 0x00, 0b_0011_0101,
            0x48, 0x00, 0b_0100_0010);

        IReadOnlyList<EffectStatus> result = FractalProtocol.ParseStatusDump(frame, FractalDeviceModel.AxeFxIII);

        Assert.Equal(new EffectStatus(35, true, 3, 3), result[0]);
        Assert.Equal(new EffectStatus(72, false, 2, 4), result[1]);
    }

    [Fact]
    public void AssemblerHandlesFragmentationCombinationAndRealtimeBytes()
    {
        var assembler = new SysexAssembler();
        byte[] first = FractalProtocol.BuildScene(FractalDeviceModel.FM9, 1);
        byte[] second = FractalProtocol.BuildScene(FractalDeviceModel.FM9, 8);

        Assert.Empty(assembler.Feed(first.AsSpan(0, 4)));
        IReadOnlyList<byte[]> frames = assembler.Feed([0xF8, .. first[4..], .. second]);

        Assert.Equal(2, frames.Count);
        Assert.Equal(first, frames[0]);
        Assert.Equal(second, frames[1]);
    }

    private static byte[] NameResponse(FractalDeviceModel model, byte opcode, byte[] address, string name)
    {
        var text = Enumerable.Repeat((byte)' ', 32).ToArray();
        Encoding.ASCII.GetBytes(name).CopyTo(text, 0);
        return FractalProtocol.Frame(model, opcode, [.. address, .. text]);
    }

    private static string Hex(byte[] frame) => string.Join(' ', frame.Select(value => value.ToString("X2")));
}

