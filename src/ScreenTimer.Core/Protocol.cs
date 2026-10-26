namespace ScreenTimer.Core;

/// <summary>One JSON line per request and per response over the named pipe.</summary>
public static class Protocol
{
    public const string PipeName = "ScreenTimer.Control";
    public const string StatusCommand = "status";
    public const string ExtendCommand = "extend";
}

public sealed class Request
{
    public string Cmd { get; set; } = Protocol.StatusCommand;
    public int Minutes { get; set; }
    public string? Code { get; set; }
}

public sealed class Response
{
    public StatusDto? Status { get; set; }
    public ExtendResult? Result { get; set; }
    public double LockoutSec { get; set; }
}
