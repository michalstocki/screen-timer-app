using System;
using System.IO;
using ScreenTimer.Core;

namespace ScreenTimer.Service;

internal static class ServiceLog
{
    const long MaxBytes = 512 * 1024;
    static readonly object Gate = new();
    static string Path_ => Path.Combine(ConfigStore.Directory, "service.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ConfigStore.Directory);
                var info = new FileInfo(Path_);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(Path_, Path_ + ".old", overwrite: true);
                File.AppendAllText(Path_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never take the service down.
        }
    }
}
