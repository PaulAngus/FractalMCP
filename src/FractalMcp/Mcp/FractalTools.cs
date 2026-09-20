using System.ComponentModel;
using FractalMcp.Midi;
using FractalMcp.Protocol;
using FractalMcp.Services;
using ModelContextProtocol.Server;

namespace FractalMcp.Mcp;

[McpServerToolType]
public sealed class FractalTools
{
    [McpServerTool(Name = "list_midi_ports", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List MIDI input and output ports visible to this computer. Use the returned opaque port IDs with connect_device.")]
    public static PortsResult ListMidiPorts(FractalSession session) =>
        new(session.GetInputPorts(), session.GetOutputPorts());

    [McpServerTool(Name = "connect_device", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Open bidirectional MIDI ports for an Axe-Fx III, FM3, or FM9. A bidirectional connection is required for queries.")]
    public static async Task<ConnectionResult> ConnectDevice(
        FractalSession session,
        [Description("Opaque input port ID from list_midi_ports.")] string inputPortId,
        [Description("Opaque output port ID from list_midi_ports.")] string outputPortId,
        [Description("Device model: axe-fx-iii, fm3, or fm9.")] string model,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel parsed = FractalDeviceModelExtensions.ParseModel(model);
        await session.ConnectAsync(inputPortId, outputPortId, parsed, cancellationToken).ConfigureAwait(false);
        return Status(session);
    }

    [McpServerTool(Name = "disconnect_device", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Close the currently open MIDI input and output ports.")]
    public static async Task<ConnectionResult> DisconnectDevice(FractalSession session, CancellationToken cancellationToken)
    {
        await session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        return Status(session);
    }

    [McpServerTool(Name = "get_connection", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Report the current MIDI connection and selected Fractal device model.")]
    public static ConnectionResult GetConnection(FractalSession session) => Status(session);

    [McpServerTool(Name = "select_preset", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Select a zero-based preset using MIDI Bank Select CC#0 followed by Program Change. This does not save or edit the preset.")]
    public static async Task<ActionResult> SelectPreset(
        FractalSession session,
        [Description("Zero-based preset number, 0-511.")] int preset,
        [Description("MIDI channel, 1-16.")] int midiChannel = 1,
        CancellationToken cancellationToken = default)
    {
        await session.SelectPresetAsync(preset, midiChannel, cancellationToken).ConfigureAwait(false);
        return new(true, $"Selected preset {preset} on MIDI channel {midiChannel}.");
    }

    [McpServerTool(Name = "select_scene", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Select Scene 1-8 using Fractal's documented SysEx command. This does not require a Scene Select CC assignment.")]
    public static async Task<SceneResult> SelectScene(
        FractalSession session,
        [Description("Scene number, 1-8.")] int scene,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildScene(model, scene), FractalProtocol.GetSetScene, cancellationToken).ConfigureAwait(false);
        return new(FractalProtocol.ParseScene(response, model));
    }

    [McpServerTool(Name = "get_current_scene", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get the active scene number, returned as 1-8.")]
    public static async Task<SceneResult> GetCurrentScene(FractalSession session, CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildScene(model), FractalProtocol.GetSetScene, cancellationToken).ConfigureAwait(false);
        return new(FractalProtocol.ParseScene(response, model));
    }

    [McpServerTool(Name = "get_preset_name", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get the name of the current preset, or of a specified zero-based preset slot.")]
    public static async Task<PresetNameResult> GetPresetName(
        FractalSession session,
        [Description("Optional zero-based preset slot, 0-511. Omit for the current preset.")] int? preset = null,
        CancellationToken cancellationToken = default)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildPresetNameQuery(model, preset), FractalProtocol.QueryPresetName, cancellationToken).ConfigureAwait(false);
        (int number, string name) = FractalProtocol.ParsePresetName(response, model);
        return new(number, name);
    }

    [McpServerTool(Name = "get_scene_name", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get a scene name from the active preset. Omit scene to get the current scene's name.")]
    public static async Task<SceneNameResult> GetSceneName(
        FractalSession session,
        [Description("Optional scene number, 1-8. Omit for the current scene.")] int? scene = null,
        CancellationToken cancellationToken = default)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildSceneNameQuery(model, scene), FractalProtocol.QuerySceneName, cancellationToken).ConfigureAwait(false);
        (int number, string name) = FractalProtocol.ParseSceneName(response, model);
        return new(number, name);
    }

    [McpServerTool(Name = "get_block_bypass", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get the bypass state of an effect block by its Fractal effect ID.")]
    public static async Task<BlockBypassResult> GetBlockBypass(
        FractalSession session,
        [Description("Fractal effect ID, obtainable from get_preset_status.")] int effectId,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildBypass(model, effectId), FractalProtocol.GetSetBypass, cancellationToken).ConfigureAwait(false);
        (int id, bool bypassed) = FractalProtocol.ParseBypass(response, model);
        return new(id, bypassed);
    }

    [McpServerTool(Name = "set_block_bypass", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Engage or bypass an effect block in the active preset. This changes live state but does not store the preset.")]
    public static async Task<BlockBypassResult> SetBlockBypass(
        FractalSession session,
        [Description("Fractal effect ID, obtainable from get_preset_status.")] int effectId,
        [Description("True to bypass the block; false to engage it.")] bool bypassed,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildBypass(model, effectId, bypassed), FractalProtocol.GetSetBypass, cancellationToken).ConfigureAwait(false);
        (int id, bool actual) = FractalProtocol.ParseBypass(response, model);
        return new(id, actual);
    }

    [McpServerTool(Name = "get_block_channel", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get an effect block's current channel by Fractal effect ID.")]
    public static async Task<BlockChannelResult> GetBlockChannel(
        FractalSession session,
        [Description("Fractal effect ID, obtainable from get_preset_status.")] int effectId,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildChannel(model, effectId), FractalProtocol.GetSetChannel, cancellationToken).ConfigureAwait(false);
        (int id, int channel) = FractalProtocol.ParseChannel(response, model);
        return new(id, channel);
    }

    [McpServerTool(Name = "set_block_channel", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Set an effect block's channel to 1-4. This changes live state but does not store the preset.")]
    public static async Task<BlockChannelResult> SetBlockChannel(
        FractalSession session,
        [Description("Fractal effect ID, obtainable from get_preset_status.")] int effectId,
        [Description("Block channel, 1-4.")] int channel,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildChannel(model, effectId, channel), FractalProtocol.GetSetChannel, cancellationToken).ConfigureAwait(false);
        (int id, int actual) = FractalProtocol.ParseChannel(response, model);
        return new(id, actual);
    }

    [McpServerTool(Name = "get_preset_status", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get all effect IDs, bypass states, active channels, and supported channel counts in the current preset.")]
    public static async Task<PresetStatusResult> GetPresetStatus(FractalSession session, CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildStatusDump(model), FractalProtocol.StatusDump, cancellationToken).ConfigureAwait(false);
        return new(FractalProtocol.ParseStatusDump(response, model));
    }

    [McpServerTool(Name = "get_tempo", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get the current device tempo in beats per minute.")]
    public static async Task<TempoResult> GetTempo(FractalSession session, CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildTempo(model), FractalProtocol.GetSetTempo, cancellationToken).ConfigureAwait(false);
        return new(FractalProtocol.ParseTempo(response, model));
    }

    [McpServerTool(Name = "set_tempo", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Set the device tempo in beats per minute.")]
    public static async Task<TempoResult> SetTempo(
        FractalSession session,
        [Description("Tempo in BPM, 20-250.")] int bpm,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildTempo(model, bpm), FractalProtocol.GetSetTempo, cancellationToken).ConfigureAwait(false);
        return new(FractalProtocol.ParseTempo(response, model));
    }

    [McpServerTool(Name = "tap_tempo", Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Send one remote tap-tempo press.")]
    public static async Task<ActionResult> TapTempo(FractalSession session, CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        await session.SendAsync(FractalProtocol.BuildTapTempo(model), cancellationToken).ConfigureAwait(false);
        return new(true, "Sent one tempo tap.");
    }

    [McpServerTool(Name = "set_tuner", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Turn the device tuner on or off.")]
    public static async Task<ActionResult> SetTuner(
        FractalSession session,
        [Description("True to turn the tuner on; false to turn it off.")] bool enabled,
        CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        await session.SendAsync(FractalProtocol.BuildTuner(model, enabled), cancellationToken).ConfigureAwait(false);
        return new(true, enabled ? "Tuner enabled." : "Tuner disabled.");
    }

    [McpServerTool(Name = "get_looper_state", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get the current Looper record, play, overdub, once, reverse, and half-speed states.")]
    public static async Task<LooperState> GetLooperState(FractalSession session, CancellationToken cancellationToken)
    {
        FractalDeviceModel model = RequireModel(session);
        byte[] response = await session.ExchangeAsync(FractalProtocol.BuildLooper(model), FractalProtocol.GetSetLooper, cancellationToken).ConfigureAwait(false);
        return FractalProtocol.ParseLooper(response, model);
    }

    [McpServerTool(Name = "press_looper_control", Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Press one Looper control: record, play, undo, once, reverse, or half-speed.")]
    public static async Task<ActionResult> PressLooperControl(
        FractalSession session,
        [Description("One of: record, play, undo, once, reverse, half-speed.")] string action,
        CancellationToken cancellationToken)
    {
        int value = action.Trim().ToLowerInvariant() switch
        {
            "record" => 0,
            "play" => 1,
            "undo" => 2,
            "once" => 3,
            "reverse" => 4,
            "half-speed" or "halfspeed" => 5,
            _ => throw new ArgumentException("Looper action must be record, play, undo, once, reverse, or half-speed.", nameof(action)),
        };
        FractalDeviceModel model = RequireModel(session);
        await session.SendAsync(FractalProtocol.BuildLooper(model, value), cancellationToken).ConfigureAwait(false);
        return new(true, $"Pressed Looper {action}.");
    }

    private static FractalDeviceModel RequireModel(FractalSession session) => session.Model ??
        throw new InvalidOperationException("Not connected. Call list_midi_ports, then connect_device first.");

    private static ConnectionResult Status(FractalSession session) => new(
        session.IsConnected,
        session.Model?.WireName(),
        session.InputPortId,
        session.OutputPortId);
}

public sealed record PortsResult(IReadOnlyList<MidiPortInfo> Inputs, IReadOnlyList<MidiPortInfo> Outputs);
public sealed record ConnectionResult(bool Connected, string? Model, string? InputPortId, string? OutputPortId);
public sealed record ActionResult(bool Success, string Message);
public sealed record SceneResult(int Scene);
public sealed record PresetNameResult(int Preset, string Name);
public sealed record SceneNameResult(int Scene, string Name);
public sealed record BlockBypassResult(int EffectId, bool Bypassed);
public sealed record BlockChannelResult(int EffectId, int Channel);
public sealed record PresetStatusResult(IReadOnlyList<EffectStatus> Effects);
public sealed record TempoResult(int Bpm);
