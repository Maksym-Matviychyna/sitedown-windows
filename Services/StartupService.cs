using System.IO;
using System.Text;

namespace SiteDownWindows.Services;

public sealed class StartupService
{
    private const string StartupFileName = "SiteDown.cmd";

    public string StartupFilePath
    {
        get
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startupFolder, StartupFileName);
        }
    }

    public void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        var startupFolder = Path.GetDirectoryName(StartupFilePath);
        if (!string.IsNullOrWhiteSpace(startupFolder))
        {
            Directory.CreateDirectory(startupFolder);
        }

        var command = new StringBuilder();
        command.AppendLine("@echo off");
        command.AppendLine("start \"SiteDown\" /min \"" + exePath + "\" --start-minimized");

        File.WriteAllText(StartupFilePath, command.ToString(), Encoding.UTF8);
    }
}
