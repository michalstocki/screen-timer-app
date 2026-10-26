using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenTimer.Core;
using ScreenTimer.Native;

namespace ScreenTimer.Service;

/// <summary>Serves the tray apps: they ask for their own status and send extension requests.</summary>
internal sealed class PipeServer
{
    readonly TimerEngine engine;
    readonly AppLauncher launcher;
    readonly Func<string, bool> isLimited;
    readonly Action stateChanged;
    readonly CancellationTokenSource cts = new();
    Task? loop;

    public PipeServer(TimerEngine engine, AppLauncher launcher, Func<string, bool> isLimited, Action stateChanged)
    {
        this.engine = engine;
        this.launcher = launcher;
        this.isLimited = isLimited;
        this.stateChanged = stateChanged;
    }

    public void Start() => loop = Task.Run(AcceptLoop);

    public void Stop()
    {
        cts.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }

    static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Read/write only: no CreateNewInstance, so a user cannot stand up a rival server.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    async Task AcceptLoop()
    {
        var security = CreateSecurity();
        while (!cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(Protocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 0, 0, security);
                await pipe.WaitForConnectionAsync(cts.Token);
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => Handle(connected));
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                ServiceLog.Write($"Pipe accept failed: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    async Task Handle(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(timeout.Token);
                var request = line is null ? null : JsonSerializer.Deserialize<Request>(line);
                if (request is null) return;

                string? sid = null;
                pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent().User?.Value);
                if (sid is null) return;

                if (NativeMethods.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var sessionId))
                    launcher.Heartbeat((int)sessionId);

                var response = Process(request, sid);
                var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException)
            {
                // Client went away or sent garbage; nothing to do.
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"Pipe request failed: {ex}");
            }
        }
    }

    Response Process(Request request, string sid)
    {
        var now = DateTime.UtcNow;
        if (!isLimited(sid))
            return new Response { Status = engine.GetStatus("unlimited:" + sid, now) };

        if (request.Cmd == Protocol.ExtendCommand)
        {
            var result = engine.TryExtend(sid, request.Minutes, request.Code, now, out var lockout);
            if (result == ExtendResult.Ok) stateChanged();
            ServiceLog.Write($"Extend {request.Minutes} min for {sid}: {result}");
            return new Response { Result = result, LockoutSec = lockout, Status = engine.GetStatus(sid, now) };
        }

        return new Response { Status = engine.GetStatus(sid, now) };
    }
}
