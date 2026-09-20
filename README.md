# Fractal MCP

An operating-system- and AI-client-agnostic Model Context Protocol server for bidirectional MIDI control of the Fractal Audio Axe-Fx III, FM3, and FM9.

The server supports standard MCP stdio for same-machine use and Streamable HTTP for running on a different computer attached to the Fractal device. It has no dependency on a particular AI vendor or model. MIDI access is abstracted behind `IMidiTransport`; the included `managed-midi` implementation uses WinMM on Windows, CoreMIDI on macOS, and ALSA on Linux.

## Current tools

- Discover MIDI inputs and outputs; connect, inspect, and disconnect.
- Select presets 0-511 with Bank Select plus Program Change.
- Get or select Scenes 1-8 using Fractal SysEx, without requiring a user-assigned Scene Select CC.
- Read preset and scene names.
- Read or change block bypass and channel state by effect ID.
- Read the current preset's effect status dump.
- Get/set tempo, tap tempo, turn the tuner on/off, and query/control the Looper.

State-changing tools change the live device state only. This initial server deliberately has no preset-store, rename, firmware, raw-SysEx, or file-import tool.

## Build and run

Requirements:

- .NET 10 SDK or newer to build. The self-contained Windows publish does not require .NET on the FM9 computer.
- A bidirectional MIDI connection to the Fractal device. Axe-Fx III and FM9 expose MIDI over USB; a two-way DIN MIDI interface also works. The FM3 is not a USB MIDI device, so it requires a bidirectional DIN MIDI interface.
- On Linux, ALSA must be available. On Windows, install Fractal's USB driver if the device requires it.

```powershell
dotnet restore FractalMcp.slnx --configfile NuGet.Config
dotnet test FractalMcp.slnx --no-restore
dotnet run --project src/FractalMcp/FractalMcp.csproj --no-restore
```

Running the last command directly appears to do nothing: an MCP stdio server waits for a host to send protocol messages. Logs go to stderr because stdout is reserved for MCP.

## Same-computer MCP host configuration

Build the server first, then configure the host to launch the DLL by absolute path:

```json
{
  "mcpServers": {
    "fractal": {
      "command": "dotnet",
      "args": [
        "C:/absolute/path/to/FractalMCP/src/FractalMcp/bin/Release/net10.0/fractal-mcp.dll"
      ]
    }
  }
}
```

Host configuration keys differ slightly, but the executable contract is standard MCP stdio. The server does not read host- or model-specific environment variables.

## Remote computer attached to the Fractal device

Use this layout when the AI/MCP client and the Fractal device are on different computers:

```text
AI client / MCP host
        |
        | MCP Streamable HTTP
        v
FM9 computer: fractal-mcp server -> MIDI -> FM9
```

### Publish for a Windows x64 FM9 computer

Run the included script from the repository root:

```powershell
.\scripts\Publish-WindowsX64.ps1
```

The output is written to `artifacts\fractal-mcp-win-x64`. It is self-contained, so the destination computer does not need .NET installed. Copy the entire output directory to the computer attached to the FM9.

The script runs this command, recorded here for CI or manual publishing:

```powershell
dotnet publish .\src\FractalMcp\FractalMcp.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --property:PublishSingleFile=true `
    --property:IncludeNativeLibrariesForSelfExtract=true `
    --property:DebugType=None `
    --property:DebugSymbols=false `
    --output .\artifacts\fractal-mcp-win-x64
```

For a different platform, replace `win-x64` with the appropriate .NET runtime identifier, such as `linux-x64`, `osx-x64`, or `osx-arm64`, and change the output directory name.

### Run on the FM9 computer

Start the server from the copied directory. This example listens on TCP port 8080:

```powershell
.\fractal-mcp.exe --transport http --urls http://0.0.0.0:8080
```

A health check is available at `http://fm9-computer:8080/health`; the MCP endpoint is `http://fm9-computer:8080/mcp`.

If Windows Firewall blocks the connection, allow inbound TCP 8080 on the Private profile from an elevated PowerShell prompt:

```powershell
New-NetFirewallRule -DisplayName "Fractal MCP" `
    -Direction Inbound -Protocol TCP -LocalPort 8080 `
    -Action Allow -Profile Private
```

On Linux or macOS, run the executable produced for that platform with the same `--transport http --urls ...` arguments.

### Configure Codex on the AI computer

If a local stdio registration named `fractal` already exists, remove it. Then register the remote URL:

```powershell
codex mcp remove fractal

codex mcp add fractal `
    --url http://fm9-computer:8080/mcp

codex mcp list
```

Restart the MCP host after changing its configuration. Other MCP clients can use the same unauthenticated `/mcp` URL. Keep the server on a trusted private network and do not port-forward port 8080 to the public internet.

Typical first calls are:

1. `list_midi_ports`
2. `connect_device` with the returned input/output IDs and `axe-fx-iii`, `fm3`, or `fm9`
3. A read such as `get_preset_name` or `get_current_scene`

Only one program may be able to hold a MIDI port on older Windows MIDI drivers. If connection fails, close the relevant Fractal editor, DAW, or other MIDI utility and try again.

## Architecture

```text
MCP host (any vendor/model)
        | MCP stdio, or Streamable HTTP
FractalTools (typed MCP contract)
        |
FractalSession (one request at a time, timeout, late-reply quarantine)
        |
FractalProtocol (pure framing/parsing; model byte 10/11/12 hex)
        |
IMidiTransport
        |
WinMM / CoreMIDI / ALSA
        |
Axe-Fx III / FM3 / FM9
```

SysEx queries are serialized because the documented protocol has no transaction IDs. Incoming data is reassembled across split callbacks, real-time bytes are ignored inside SysEx, unrelated frames are skipped, and timed-out requests quarantine possible late replies before the next query. These safeguards are adapted from the proven transport/query structure in `PresetMaestro`; its NAudio transport itself was not copied because it is Windows-specific.

## Device numbering

- MCP preset numbers are zero-based (`0-511`), matching the MIDI wire protocol.
- MCP scene numbers and block channels are one-based for humans (`1-8` and `1-4`); the server translates them to zero-based wire values.
- MIDI channels are one-based (`1-16`).
- Effect IDs are Fractal's numeric IDs. Call `get_preset_status` to discover the IDs present in the active preset.

## Sources and scope

Protocol implementation is based on Fractal Audio's official [Axe-Fx III MIDI for Third-Party Devices, revision 1.4](https://fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf). The common framing is `F0 00 01 74 mm ... checksum F7`. The official guide documents Axe-Fx III model byte `10`; FM3 byte `11` and FM9 byte `12` are corroborated by the community-maintained [Fractal Audio SysEx reference](https://wiki.fractalaudio.com/wiki/index.php?title=MIDI_SysEx) and the existing FM9 code examined for this project. The current-generation owner manuals document standard MIDI Program Change, Bank Select, assignable CCs, MIDI clock reception, and device-specific USB MIDI behavior:

- [Axe-Fx III Owner's Manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-3/Axe-Fx-III-Owners-Manual.pdf)
- [FM9 Owner's Manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf)
- [FM3 Owner's Manual](https://www.fractalaudio.com/downloads/manuals/FM3/FM3-Owners-Manual.pdf)

The MCP layer uses the official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) and its standard stdio and Streamable HTTP server transports.

The documented third-party SysEx guide is titled for Axe-Fx III. The same command family is used by the existing FM9 implementation, but every operation should still be hardware-tested on each model/firmware combination before a production release. Automated tests validate framing, checksums, model isolation, parsing, fragmented input, request filtering, and Bank/PC translation; they do not substitute for physical device verification. This development machine verified Windows MIDI enumeration. macOS/CoreMIDI and Linux/ALSA builds and physical-device behavior remain to be verified on those operating systems.
