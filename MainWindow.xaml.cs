using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Media;
using System.Windows;
using System.Windows.Documents;
using SiteDownWindows.Models;
using SiteDownWindows.Services;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using Media = System.Windows.Media;

namespace SiteDownWindows;

public partial class MainWindow : Window
{
    private const int CURRENT_VERSION_CODE = 5;
    private const string CURRENT_VERSION = "1.0.5";
    private const string DOWNLOAD_URL = "https://www.sitedown.app/download/";

    private readonly ObservableCollection<SiteMonitorItem> _sites = new();
    private readonly Logger _logger = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly PairingService _pairingService = new();
    private readonly StartupService _startupService = new();
    private readonly MonitorService _monitorService;
    private readonly WinForms.NotifyIcon _trayIcon;
    private const string SingleInstancePipeName = "SiteDownWindowsSingleInstancePipe";
    private CancellationTokenSource? _singleInstancePipeCancellationTokenSource;
    private AlertPopupWindow? _currentAlertPopup;
    private Media.MediaPlayer? _notificationPlayer;
    private bool _isReallyClosing;
    // Start minimized only when launched by the Windows Startup shortcut.
    // Normal user launch opens the main window.
    private readonly bool _startMinimized = Environment.GetCommandLineArgs()
        .Any(arg => arg.Equals("--start-minimized", StringComparison.OrdinalIgnoreCase));

    public MainWindow()
    {
        InitializeComponent();

        Title = $"SiteDown v{CURRENT_VERSION}";

        _monitorService = new MonitorService(_logger);
        _monitorService.AlertRequested += OnAlertRequested;
        _logger.MessageLogged += OnMessageLogged;

        SitesListView.ItemsSource = _sites;
        _trayIcon = CreateTrayIcon();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        StartSingleInstancePipeServer();
    }

    private void StartSingleInstancePipeServer()
    {
        _singleInstancePipeCancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => RunSingleInstancePipeServerAsync(_singleInstancePipeCancellationTokenSource.Token));
    }

    private async Task RunSingleInstancePipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    SingleInstancePipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync();

                if (message?.Equals("SHOW", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Dispatcher.Invoke(() => ShowFromTray());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsStore.LoadAsync();

        // Tokens are one-time-use. Never restore a previously entered token into the input field.
        PairKeyTextBox.Text = string.Empty;
        ReplaceSites(settings.Sites);
        TokenBlock.Visibility = _sites.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        // Re-save settings without any token value. This also cleans old settings files from earlier app versions.
        await _settingsStore.SaveAsync(new AppSettings
        {
            Sites = _sites.ToList(),
            MonitoringWasRunning = settings.MonitoringWasRunning
        });

        EnableStartupShortcut();

        if (_sites.Count > 0)
        {
            _logger.Info($"Loaded {_sites.Count} website(s) from local settings.");

            if (settings.MonitoringWasRunning)
            {
                _monitorService.Start(_sites);
                StatusTextBlock.Text = _monitorService.IsRunning ? "Running" : "Stopped";
            }
            else
            {
                StatusTextBlock.Text = "Stopped";
            }
        }
        else
        {
            StatusTextBlock.Text = "No token";
            _logger.Info("Enter a token to connect this Windows device.");
        }

        UpdateCounts();

        if (_startMinimized)
        {
            Hide();
        }
    }

    private async void PairButton_Click(object sender, RoutedEventArgs e)
    {
        await PairOrRefreshAsync(startAfterSuccess: true);
    }


    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sites.Count == 0)
        {
            _logger.Info("No websites loaded yet. Apply the token first.");
            return;
        }

        _monitorService.Start(_sites);
        StatusTextBlock.Text = "Running";
        _ = SaveMonitoringStateAsync(true);
        EnableStartupShortcut();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _monitorService.Stop();
        StatusTextBlock.Text = _sites.Count > 0 ? "Stopped" : "No token";
        _ = SaveMonitoringStateAsync(false);
        EnableStartupShortcut();
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.sitedown.app/");
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(DOWNLOAD_URL);
    }

    private void SetupButton_Click(object sender, RoutedEventArgs e)
    {
        HideHelpScreen();
        TokenBlock.Visibility = TokenBlock.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        MainContentGrid.Visibility = Visibility.Collapsed;
        HelpScreen.Visibility = Visibility.Visible;
    }

    private void HelpBackButton_Click(object sender, RoutedEventArgs e)
    {
        HideHelpScreen();
    }

    private void HideHelpScreen()
    {
        HelpScreen.Visibility = Visibility.Collapsed;
        MainContentGrid.Visibility = Visibility.Visible;
    }

    private void PairKeyTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TokenPlaceholderTextBlock == null)
        {
            return;
        }

        TokenPlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(PairKeyTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TokenSetupLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.ToString());
        e.Handled = true;
    }

    private void HelpWebsiteLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.ToString());
        e.Handled = true;
    }

    private async Task PairOrRefreshAsync(bool startAfterSuccess)
    {
        var key = PairKeyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.Error("Token is empty.");
            return;
        }

        PairButton.IsEnabled = false;
        StatusTextBlock.Text = "Saving token...";
        _logger.Info("Saving token and loading settings from SiteDown...");

        try
        {
            var wasRunning = _monitorService.IsRunning;
            var result = await _pairingService.PairAsync(key);
            if (!result.Success)
            {
                StatusTextBlock.Text = _sites.Count > 0 ? "Stopped" : "No token";
                _logger.Error(result.Message);
                return;
            }

            if (wasRunning)
            {
                _monitorService.Stop();
                await Task.Delay(500);
            }

            UpdateVersionBlock(result.LatestVersionCode);

            ReplaceSites(result.Sites);
            UpdateCounts();

            await _settingsStore.SaveAsync(new AppSettings
            {
                Sites = _sites.ToList(),
                MonitoringWasRunning = startAfterSuccess && _sites.Count > 0
            });

            // Tokens are one-time-use. Clear the input immediately after the token is applied.
            PairKeyTextBox.Text = string.Empty;
            TokenBlock.Visibility = _sites.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            _logger.Info(result.Message);
            _logger.Info($"Loaded {_sites.Count} website(s) from SiteDown.");

            if (_sites.Count == 0)
            {
                StatusTextBlock.Text = "No token";
                _logger.Info("Token was applied, but the server returned no websites yet. Create a new token when settings are ready.");
            }
            else if (startAfterSuccess)
            {
                _monitorService.Start(_sites);
                StatusTextBlock.Text = _monitorService.IsRunning ? "Running" : "Stopped";
                EnableStartupShortcut();
            }
            else
            {
                StatusTextBlock.Text = "Stopped";
                EnableStartupShortcut();
            }
        }
        finally
        {
            PairButton.IsEnabled = true;
        }
    }

    private void UpdateVersionBlock(int? latestVersionCode)
    {
        if (latestVersionCode.HasValue && latestVersionCode.Value > CURRENT_VERSION_CODE)
        {
            UpdateBlock.Visibility = Visibility.Visible;
            _logger.Info($"New version is available. Latest version code: {latestVersionCode.Value}, current version code: {CURRENT_VERSION_CODE}.");
        }
        else
        {
            UpdateBlock.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SaveMonitoringStateAsync(bool monitoringWasRunning)
    {
        try
        {
            await _settingsStore.SaveAsync(new AppSettings
            {
                Sites = _sites.ToList(),
                MonitoringWasRunning = monitoringWasRunning
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Could not save monitoring state: " + ex.Message);
        }
    }

    private void EnableStartupShortcut()
    {
        try
        {
            _startupService.Enable();
        }
        catch (Exception ex)
        {
            _logger.Error("Could not enable Start with Windows: " + ex.Message);
        }
    }

    private void ReplaceSites(IEnumerable<SiteMonitorItem> sites)
    {
        _sites.Clear();
        foreach (var site in sites)
        {
            site.NextCheck = DateTimeOffset.MinValue;
            _sites.Add(site);
        }

        UpdateCounts();
    }

    private void UpdateCounts()
    {
        SitesCountTextBlock.Text = _sites.Count.ToString();
    }

    private void OnMessageLogged(string message)
    {
        Dispatcher.Invoke(() =>
        {
            var paragraph = new Paragraph(new Run(message))
            {
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = GetLogLineBrush(message)
            };

            // Newest log records stay at the top.
            var firstBlock = LogRichTextBox.Document.Blocks.FirstBlock;
            if (firstBlock == null)
            {
                LogRichTextBox.Document.Blocks.Add(paragraph);
            }
            else
            {
                LogRichTextBox.Document.Blocks.InsertBefore(firstBlock, paragraph);
            }

            LogRichTextBox.ScrollToHome();
        });
    }

    private static System.Windows.Media.Brush GetLogLineBrush(string message)
    {
        if (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return System.Windows.Media.Brushes.LightCoral;
        }

        if (message.Contains("OK", StringComparison.OrdinalIgnoreCase))
        {
            return System.Windows.Media.Brushes.LightGreen;
        }

        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE9, 0xEB, 0xFF));
    }

    private void OnAlertRequested(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            PlayNotificationSound();

            try
            {
                _currentAlertPopup?.Close();

                _currentAlertPopup = new AlertPopupWindow(title, message, ShowFromTray);
                _currentAlertPopup.Closed += (_, _) => _currentAlertPopup = null;
                _currentAlertPopup.Show();
            }
            catch
            {
                // Fallback: show the old Windows tray balloon if the custom popup cannot be opened.
                _trayIcon.Icon = LoadIcon();
                _trayIcon.BalloonTipIcon = WinForms.ToolTipIcon.Warning;
                _trayIcon.BalloonTipTitle = title;
                _trayIcon.BalloonTipText = message;
                _trayIcon.ShowBalloonTip(7000);
            }
        });
    }

    private void PlayNotificationSound()
    {
        try
        {
            var soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "notification.mp3");
            if (!File.Exists(soundPath))
            {
                SystemSounds.Exclamation.Play();
                return;
            }

            _notificationPlayer?.Stop();
            _notificationPlayer?.Close();

            _notificationPlayer = new Media.MediaPlayer();
            _notificationPlayer.Volume = 1.0;
            _notificationPlayer.Open(new Uri(soundPath, UriKind.Absolute));
            _notificationPlayer.Play();
        }
        catch
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch
            {
                // Sound is optional.
            }
        }
    }

    private WinForms.NotifyIcon CreateTrayIcon()
    {
        var tray = new WinForms.NotifyIcon
        {
            Text = "SiteDown",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };

        tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => ShowFromTray());

        return tray;
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "sitedown.ico");
            if (File.Exists(iconPath))
            {
                return new Drawing.Icon(iconPath);
            }
        }
        catch
        {
            // Fall back to default application icon.
        }

        return Drawing.SystemIcons.Application;
    }

    private void StopSingleInstancePipeServer()
    {
        try
        {
            _singleInstancePipeCancellationTokenSource?.Cancel();
        }
        catch
        {
            // Ignore cancellation errors.
        }

        _singleInstancePipeCancellationTokenSource?.Dispose();
        _singleInstancePipeCancellationTokenSource = null;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isReallyClosing)
        {
            StopSingleInstancePipeServer();
            _trayIcon.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
        _trayIcon.Icon = LoadIcon();
        _trayIcon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
        _trayIcon.BalloonTipTitle = "SiteDown is still running";
        _trayIcon.BalloonTipText = "SiteDown continues monitoring in the system tray.";
        _trayIcon.ShowBalloonTip(3500);
    }

    private void ExitApp()
    {
        _isReallyClosing = true;
        StopSingleInstancePipeServer();
        _monitorService.Stop();
        _currentAlertPopup?.Close();
        _notificationPlayer?.Stop();
        _notificationPlayer?.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Close();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch errors.
        }
    }
}
