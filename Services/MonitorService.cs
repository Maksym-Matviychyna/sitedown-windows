using System.Net.Http;
using System.Net.NetworkInformation;
using SiteDownWindows.Models;
using System.Windows;

namespace SiteDownWindows.Services;

public sealed class MonitorService
{
    private const string InternetCheckUrl = "https://www.sitedown.app/api/checkLink.txt";
    private static readonly TimeSpan WebsiteCheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InternetCheckTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly HttpClient _internetCheckHttpClient;
    private readonly Logger _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _internetUnavailableLogged;
    private Task? _monitorTask;

    public MonitorService(Logger logger)
    {
        _logger = logger;

        // Timeout is controlled manually with linked CancellationTokenSource.
        // This allows us to distinguish a real app stop from a website timeout.
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SiteDown-Windows/1.0");

        _internetCheckHttpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _internetCheckHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SiteDown-Windows/1.0");
    }

    public bool IsRunning => _monitorTask is { IsCompleted: false };

    public event Action<string, string>? AlertRequested;

    public void Start(IList<SiteMonitorItem> sites)
    {
        if (IsRunning)
        {
            _logger.Info("Monitoring is already running.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        foreach (var site in sites)
        {
            if (site.Enabled)
            {
                site.NextCheck = DateTimeOffset.MinValue;
            }
        }

        _monitorTask = Task.Run(() => RunLoopAsync(sites, _cancellationTokenSource.Token));
        _logger.Info("Monitoring started.");
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            _logger.Info("Monitoring is already stopped.");
            return;
        }

        _cancellationTokenSource?.Cancel();
        _logger.Info("Monitoring stopped.");
    }

    private async Task RunLoopAsync(IList<SiteMonitorItem> sites, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var enabledSites = sites.Where(s => s.Enabled).ToList();

                foreach (var site in enabledSites)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (site.NextCheck > now) continue;

                    await CheckSiteAsync(site, cancellationToken);
                    site.NextCheck = DateTimeOffset.Now.AddMinutes(Math.Max(1, site.CheckIntervalMinutes));
                }

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException ex)
            {
                _logger.Error("Monitor loop cancelled unexpectedly: " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error("Monitor loop error: " + ex.Message);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task CheckSiteAsync(SiteMonitorItem site, CancellationToken cancellationToken)
    {
        UpdateSite(site, () => site.LastChecked = DateTimeOffset.Now);
        _logger.Info($"Checking {site.Name} ({site.Url})");

        try
        {
            using var websiteTimeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            websiteTimeoutTokenSource.CancelAfter(WebsiteCheckTimeout);

            using var response = await _httpClient.GetAsync(site.Url, websiteTimeoutTokenSource.Token);
            var html = await response.Content.ReadAsStringAsync(websiteTimeoutTokenSource.Token);

            // If the monitored website returned any HTTP response, the internet connection is available.
            _internetUnavailableLogged = false;

            if (!response.IsSuccessStatusCode)
            {
                UpdateSite(site, () => site.LastStatus = $"HTTP {(int)response.StatusCode}");
                Alert(site, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
                return;
            }

            if (!string.IsNullOrWhiteSpace(site.ExpectedKeyword) &&
                !html.Contains(site.ExpectedKeyword, StringComparison.OrdinalIgnoreCase))
            {
                UpdateSite(site, () => site.LastStatus = "Keyword missing");
                Alert(site, $"Keyword not found: {site.ExpectedKeyword}");
                return;
            }

            UpdateSite(site, () => site.LastStatus = "OK");
            _logger.Info($"OK: {site.Name}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await HandleConnectionProblemAsync(site, $"Timeout after {WebsiteCheckTimeout.TotalSeconds:0} seconds", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleConnectionProblemAsync(site, ex.Message, cancellationToken);
        }
    }

    private async Task HandleConnectionProblemAsync(SiteMonitorItem site, string reason, CancellationToken cancellationToken)
    {
        var internetAvailable = await IsInternetConnectionAvailableAsync(cancellationToken);
        if (!internetAvailable)
        {
            UpdateSite(site, () => site.LastStatus = "Internet unavailable");

            if (!_internetUnavailableLogged)
            {
                _logger.Error("Internet connection unavailable.");
                _internetUnavailableLogged = true;
            }

            return;
        }

        _internetUnavailableLogged = false;
        UpdateSite(site, () => site.LastStatus = "Error");
        Alert(site, reason);
    }

    private async Task<bool> IsInternetConnectionAvailableAsync(CancellationToken cancellationToken)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return false;
        }

        try
        {
            using var internetTimeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            internetTimeoutTokenSource.CancelAfter(InternetCheckTimeout);

            using var response = await _internetCheckHttpClient.GetAsync(InternetCheckUrl, internetTimeoutTokenSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var responseText = await response.Content.ReadAsStringAsync(internetTimeoutTokenSource.Token);
            return responseText.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static void UpdateSite(SiteMonitorItem site, Action update)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            update();
        }
        else
        {
            dispatcher.Invoke(update);
        }
    }

    private void Alert(SiteMonitorItem site, string reason)
    {
        var title = $"SiteDown alert: {site.Name}";
        var message = $"{site.Url}\n{reason}";
        _logger.Error($"{site.Name}: {reason}");
        AlertRequested?.Invoke(title, message);
    }
}
