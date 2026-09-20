namespace FractalMcp.Midi;

public sealed record MidiPortInfo(string Id, string Name, string Manufacturer, string Version);

public sealed class MidiMessageEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}

public interface IMidiTransport : IAsyncDisposable
{
    event EventHandler<MidiMessageEventArgs>? MessageReceived;

    bool IsOpen { get; }
    string? InputPortId { get; }
    string? OutputPortId { get; }

    IReadOnlyList<MidiPortInfo> GetInputPorts();
    IReadOnlyList<MidiPortInfo> GetOutputPorts();
    Task OpenAsync(string inputPortId, string outputPortId, CancellationToken cancellationToken);
    Task CloseAsync();
    void Send(byte[] message);
}

