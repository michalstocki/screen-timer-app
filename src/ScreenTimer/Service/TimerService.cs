using System.ServiceProcess;

namespace ScreenTimer.Service;

internal sealed class TimerService : ServiceBase
{
    public const string Name = "ScreenTimerService";

    Supervisor? supervisor;

    public TimerService()
    {
        ServiceName = Name;
        CanHandleSessionChangeEvent = true;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        supervisor = new Supervisor();
        supervisor.Start();
    }

    protected override void OnStop() => supervisor?.Stop();

    protected override void OnShutdown() => supervisor?.Stop();

    protected override void OnSessionChange(SessionChangeDescription change) =>
        supervisor?.OnSessionChange(change.Reason, change.SessionId);
}
