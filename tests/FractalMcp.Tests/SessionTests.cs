using FractalMcp.Midi;
using FractalMcp.Protocol;
using FractalMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FractalMcp.Tests;

public sealed class SessionTests
{
    [Fact]
    [Trait("Category", "PlatformSmoke")]
    public async Task ManagedMidiBackendCanEnumerateThisOperatingSystem()
    {
        await using var transport = new ManagedMidiTransport();

        Assert.NotNull(transport.GetInputPorts());
        Assert.NotNull(transport.GetOutputPorts());
    }

    [Fact]
    public async Task SelectPresetSendsBankThenProgramChange()
    {
        await using var transport = new FakeMidiTransport();
        await using var session = new FractalSession(transport, NullLogger<FractalSession>.Instance);
        await session.ConnectAsync("in", "out", FractalDeviceModel.FM9, CancellationToken.None);

        await session.SelectPresetAsync(383, 2, CancellationToken.None);

        Assert.Equal(new byte[] { 0xB1, 0x00, 0x02 }, transport.Sent[0]);
        Assert.Equal(new byte[] { 0xC1, 0x7F }, transport.Sent[1]);
    }

    [Fact]
    public async Task ExchangeIgnoresUnrelatedFrames()
    {
        await using var transport = new FakeMidiTransport();
        await using var session = new FractalSession(transport, NullLogger<FractalSession>.Instance);
        await session.ConnectAsync("in", "out", FractalDeviceModel.FM3, CancellationToken.None);
        transport.OnSend = _ =>
        {
            transport.Reply(FractalProtocol.Frame(FractalDeviceModel.FM3, FractalProtocol.GetSetTempo, 120, 0));
            transport.Reply(FractalProtocol.Frame(FractalDeviceModel.FM3, FractalProtocol.GetSetScene, 4));
        };

        byte[] frame = await session.ExchangeAsync(
            FractalProtocol.BuildScene(FractalDeviceModel.FM3),
            FractalProtocol.GetSetScene,
            CancellationToken.None);

        Assert.Equal(5, FractalProtocol.ParseScene(frame, FractalDeviceModel.FM3));
    }

    private sealed class FakeMidiTransport : IMidiTransport
    {
        public event EventHandler<MidiMessageEventArgs>? MessageReceived;
        public bool IsOpen { get; private set; }
        public string? InputPortId { get; private set; }
        public string? OutputPortId { get; private set; }
        public List<byte[]> Sent { get; } = [];
        public Action<byte[]>? OnSend { get; set; }

        public IReadOnlyList<MidiPortInfo> GetInputPorts() => [new("in", "Input", "Test", "1")];
        public IReadOnlyList<MidiPortInfo> GetOutputPorts() => [new("out", "Output", "Test", "1")];

        public Task OpenAsync(string inputPortId, string outputPortId, CancellationToken cancellationToken)
        {
            IsOpen = true;
            InputPortId = inputPortId;
            OutputPortId = outputPortId;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            IsOpen = false;
            InputPortId = null;
            OutputPortId = null;
            return Task.CompletedTask;
        }

        public void Send(byte[] message)
        {
            Sent.Add(message);
            OnSend?.Invoke(message);
        }

        public void Reply(byte[] message) => MessageReceived?.Invoke(this, new MidiMessageEventArgs(message));
        public async ValueTask DisposeAsync() => await CloseAsync();
    }
}
