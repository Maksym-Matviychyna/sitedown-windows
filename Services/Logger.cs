namespace SiteDownWindows.Services;

public sealed class Logger
{
    public event Action<string>? MessageLogged;

    public void Info(string message) => Write(message);

    public void Error(string message) => Write("ERROR: " + message);

    private void Write(string message)
    {
        MessageLogged?.Invoke($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }
}
