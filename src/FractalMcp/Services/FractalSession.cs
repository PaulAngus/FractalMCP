using System.Threading.Channels;
using FractalMcp.Midi;
using FractalMcp.Protocol;
using Microsoft.Extensions.Logging;

namespace FractalMcp.Services;

public sealed class FractalSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly IMidiTransport _transport;
    private readonly ILogger<FractalSession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SysexAssembler _assembler = new();
    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = false,
    });
    private long _quietUntil;

    public FractalSession(IMidiTransport transport, ILogger<FractalSession> logger)
    {
        _transport = transport;
        _logger = logger;
        _transport.MessageReceived += OnMessageReceived;
    }

    public FractalDeviceModel? Model { get; private set; }
    public bool IsConnected => _transport.IsOpen && Model.HasValue;
    public string? InputPortId => _transport.InputPortId;
    public string? OutputPortId => _transport.OutputPortId;

    public IReadOnlyList<MidiPortInfo> GetInputPorts() => _transport.GetInputPorts();
    public IReadOnlyList<MidiPortInfo> GetOutputPorts() => _transport.GetOutputPorts();

    public async Task ConnectAsync(string inputPortId, string outputPortId, FractalDeviceModel model, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Model = null;
            await _transport.OpenAsync(inputPortId, outputPortId, cancellationToken).ConfigureAwait(false);
            Model = model;
            DrainFrames();
            _logger.LogInformation("Connected MIDI input {Input} and output {Output} for {Model}", inputPortId, outputPortId, model.WireName());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.CloseAsync().ConfigureAwait(false);
            Model = null;
            DrainFrames();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<byte[]> ExchangeAsync(byte[] request, byte responseOpcode, CancellationToken cancellationToken) =>
        ExchangeAsync(request, responseOpcode, DefaultTimeout, cancellationToken);

    public async Task<byte[]> ExchangeAsync(byte[] request, byte responseOpcode, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FractalDeviceModel model = RequireConnected();
            long quietRemaining = Interlocked.Read(ref _quietUntil) - Environment.TickCount64;
            if (quietRemaining > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(quietRemaining), cancellationToken).ConfigureAwait(false);
            }

            DrainFrames();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            bool completed = false;
            try
            {
                _transport.Send(request);
                while (true)
                {
                    byte[] frame = await _frames.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                    if (FractalProtocol.IsValidFrame(frame, model, responseOpcode))
                    {
                        completed = true;
                        return frame;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Fractal MIDI request 0x{request[5]:X2} timed out after {timeout.TotalSeconds:0.#} seconds.");
            }
            finally
            {
                if (!completed)
                {
                    Interlocked.Exchange(ref _quietUntil, Environment.TickCount64 + (long)timeout.TotalMilliseconds);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireConnected();
            _transport.Send(message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SelectPresetAsync(int preset, int midiChannel, CancellationToken cancellationToken)
    {
        if (preset is < 0 or > 511)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), "Preset must be 0-511.");
        }

        if (midiChannel is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(midiChannel), "MIDI channel must be 1-16.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireConnected();
            byte channel = (byte)(midiChannel - 1);
            _transport.Send([(byte)(0xB0 | channel), 0x00, (byte)(preset / 128)]);
            _transport.Send([(byte)(0xC0 | channel), (byte)(preset % 128)]);
        }
        finally
        {
            _gate.Release();
        }
    }

    private FractalDeviceModel RequireConnected() => IsConnected
        ? Model!.Value
        : throw new InvalidOperationException("Not connected. Call list_midi_ports, then connect_device first.");

    private void OnMessageReceived(object? sender, MidiMessageEventArgs args)
    {
        lock (_assembler)
        {
            foreach (byte[] frame in _assembler.Feed(args.Data))
            {
                _frames.Writer.TryWrite(frame);
            }
        }
    }

    private void DrainFrames()
    {
        while (_frames.Reader.TryRead(out _))
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _transport.MessageReceived -= OnMessageReceived;
        await _transport.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
