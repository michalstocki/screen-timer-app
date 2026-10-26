using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTimer.Core;

namespace ScreenTimer.Client;

internal sealed record ExtendOutcome(ExtendResult Result, double LockoutSec);

internal interface IStateClient
{
    /// <summary>Null when the service cannot be reached.</summary>
    Task<StatusDto?> GetStatusAsync();

    Task<ExtendOutcome?> ExtendAsync(int minutes, string code);
}

internal sealed class PipeStateClient : IStateClient
{
    public Task<StatusDto?> GetStatusAsync() => Send(new Request { Cmd = Protocol.StatusCommand }, r => r.Status);

    public Task<ExtendOutcome?> ExtendAsync(int minutes, string code) =>
        Send(new Request { Cmd = Protocol.ExtendCommand, Minutes = minutes, Code = code },
            r => r.Result is { } result ? new ExtendOutcome(result, r.LockoutSec) : null);

    static async Task<T?> Send<T>(Request request, Func<Response, T?> select) where T : class
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(cts.Token);

            var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));

            var line = await new StreamReader(pipe).ReadLineAsync(cts.Token);
            var response = line is null ? null : JsonSerializer.Deserialize<Response>(line);
            return response is null ? null : select(response);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>In-process stand-in for the service, used by <c>--dev</c> to try the UI quickly.</summary>
internal sealed class DevStateClient : IStateClient
{
    const string Sid = "dev";

    readonly TimerEngine engine;
    readonly Timer ticker;

    public static AppConfig CreateConfig()
    {
        var cfg = new AppConfig { SessionMinutes = 0.6, BreakMinutes = 0.6, WarningMinutes = 0.3 };
        cfg.SetCode("1234");
        return cfg;
    }

    public DevStateClient()
    {
        var cfg = CreateConfig();
        engine = new TimerEngine(() => cfg);
        var observed = new System.Collections.Generic.Dictionary<string, ObservedAccount>
        {
            [Sid] = new ObservedAccount("dev", true),
        };
        engine.Tick(DateTime.UtcNow, observed);
        ticker = new Timer(_ => engine.Tick(DateTime.UtcNow, observed), null, 1000, 1000);
    }

    public Task<StatusDto?> GetStatusAsync() => Task.FromResult<StatusDto?>(engine.GetStatus(Sid, DateTime.UtcNow));

    public Task<ExtendOutcome?> ExtendAsync(int minutes, string code)
    {
        var result = engine.TryExtend(Sid, minutes, code, DateTime.UtcNow, out var lockout);
        return Task.FromResult<ExtendOutcome?>(new ExtendOutcome(result, lockout));
    }
}
