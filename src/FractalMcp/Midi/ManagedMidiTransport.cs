using Commons.Music.Midi;

namespace FractalMcp.Midi;

public sealed class ManagedMidiTransport : IMidiTransport
{
#pragma warning disable CS0618 // Package 1.10.1's concrete desktop backends implement IMidiAccess, not IMidiAccess2.
    private readonly IMidiAccess _access = MidiAccessManager.Default;
#pragma warning restore CS0618
    private readonly object _sendLock = new();
    private IMidiInput? _input;
    private IMidiOutput? _output;

    public event EventHandler<MidiMessageEventArgs>? MessageReceived;

    public bool IsOpen => _input is not null && _output is not null;
    public string? InputPortId => _input?.Details.Id;
    public string? OutputPortId => _output?.Details.Id;

    public IReadOnlyList<MidiPortInfo> GetInputPorts() => _access.Inputs.Select(ToPort).ToArray();
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => _access.Outputs.Select(ToPort).ToArray();

    public async Task OpenAsync(string inputPortId, string outputPortId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPortId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPortId);
        await CloseAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        IMidiInput input = await _access.OpenInputAsync(inputPortId).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMidiOutput output = await _access.OpenOutputAsync(outputPortId).ConfigureAwait(false);
            _input = input;
            _output = output;
            _input.MessageReceived += OnMessageReceived;
        }
        catch
        {
            await input.CloseAsync().ConfigureAwait(false);
            input.Dispose();
            throw;
        }
    }

    public async Task CloseAsync()
    {
        IMidiInput? input = _input;
        IMidiOutput? output = _output;
        _input = null;
        _output = null;
        Exception? failure = null;

        if (input is not null)
        {
            input.MessageReceived -= OnMessageReceived;
            try
            {
                await input.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                input.Dispose();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (output is not null)
        {
            try
            {
                await output.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            try
            {
                output.Dispose();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    public void Send(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_sendLock)
        {
            IMidiOutput output = _output ?? throw new InvalidOperationException("MIDI output is not connected.");
            output.Send(message, 0, message.Length, 0);
        }
    }

    private void OnMessageReceived(object? sender, MidiReceivedEventArgs args)
    {
        if (args.Data is null || args.Length <= 0)
        {
            return;
        }

        var copy = new byte[args.Length];
        Array.Copy(args.Data, args.Start, copy, 0, args.Length);
        MessageReceived?.Invoke(this, new MidiMessageEventArgs(copy));
    }

    private static MidiPortInfo ToPort(IMidiPortDetails port) =>
        new(port.Id, port.Name, port.Manufacturer, port.Version);

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}
