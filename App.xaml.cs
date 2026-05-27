using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SiteDownWindows;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\SiteDownWindowsSingleInstanceMutex";
    private const string SingleInstancePipeName = "SiteDownWindowsSingleInstancePipe";

    private Mutex? _singleInstanceMutex;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);

        if (!createdNew)
        {
            NotifyExistingInstance();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        try
        {
            SetCurrentProcessExplicitAppUserModelID("SiteDown");
        }
        catch
        {
            // Ignore if Windows does not allow setting the explicit AppUserModelID.
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Ignore release errors.
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        base.OnExit(e);
    }

    private static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                SingleInstancePipeName,
                PipeDirection.Out);

            client.Connect(800);

            using var writer = new StreamWriter(client)
            {
                AutoFlush = true
            };

            writer.WriteLine("SHOW");
        }
        catch
        {
            // If the existing instance cannot be notified, still prevent a second instance.
        }
    }
}
