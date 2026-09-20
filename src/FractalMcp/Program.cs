using FractalMcp.Mcp;
using FractalMcp.Midi;
using FractalMcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

const string TransportArgument = "--transport";

string transport = GetOption(args, TransportArgument) ??
    Environment.GetEnvironmentVariable("FRACTAL_MCP_TRANSPORT") ??
    "stdio";

string[] remainingArgs = RemoveOption(args, TransportArgument);

if (transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
{
    await RunStdioAsync(remainingArgs);
    return;
}

if (transport.Equals("http", StringComparison.OrdinalIgnoreCase))
{
    await RunHttpAsync(remainingArgs);
    return;
}

throw new ArgumentException(
    $"Unknown transport '{transport}'. Use 'stdio' or 'http'.",
    TransportArgument);

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // stdout belongs exclusively to the MCP stdio transport.
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    AddFractalServices(builder.Services);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<FractalTools>();

    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    AddFractalServices(builder.Services);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools<FractalTools>();

    var app = builder.Build();
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapMcp("/mcp");

    await app.RunAsync();
}

static void AddFractalServices(IServiceCollection services)
{
    services.AddSingleton<IMidiTransport, ManagedMidiTransport>();
    services.AddSingleton<FractalSession>();
}

static string? GetOption(string[] args, string option)
{
    for (int index = 0; index < args.Length; index++)
    {
        if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
        {
            return index + 1 < args.Length
                ? args[index + 1]
                : throw new ArgumentException($"Missing value for {option}.", option);
        }

        string prefix = option + "=";
        if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return args[index][prefix.Length..];
        }
    }

    return null;
}

static string[] RemoveOption(string[] args, string option)
{
    var remaining = new List<string>(args.Length);

    for (int index = 0; index < args.Length; index++)
    {
        if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
        {
            index++;
            continue;
        }

        if (args[index].StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        remaining.Add(args[index]);
    }

    return [.. remaining];
}
