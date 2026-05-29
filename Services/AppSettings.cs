using SiteDownWindows.Models;

namespace SiteDownWindows.Services;

public sealed class AppSettings
{
    public List<SiteMonitorItem> Sites { get; set; } = new();

    // Existing settings files from older versions do not have this value.
    // Default true means existing configured apps continue monitoring after update.
    public bool MonitoringWasRunning { get; set; } = true;
}
