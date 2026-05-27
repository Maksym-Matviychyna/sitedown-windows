using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SiteDownWindows.Models;

public sealed class SiteMonitorItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _url = string.Empty;
    private string _expectedKeyword = string.Empty;
    private int _checkIntervalMinutes = 3;
    private bool _enabled = true;
    private string _lastStatus = "Waiting";
    private DateTimeOffset? _lastChecked;
    private DateTimeOffset _nextCheck = DateTimeOffset.MinValue;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value);
    }

    public string ExpectedKeyword
    {
        get => _expectedKeyword;
        set => SetField(ref _expectedKeyword, value);
    }

    public int CheckIntervalMinutes
    {
        get => _checkIntervalMinutes;
        set
        {
            var safeValue = Math.Max(1, value);
            if (SetField(ref _checkIntervalMinutes, safeValue))
            {
                OnPropertyChanged(nameof(CheckIntervalText));
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value))
            {
                OnPropertyChanged(nameof(EnabledText));
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        set => SetField(ref _lastStatus, value);
    }

    public DateTimeOffset? LastChecked
    {
        get => _lastChecked;
        set
        {
            if (SetField(ref _lastChecked, value))
            {
                OnPropertyChanged(nameof(LastCheckedText));
            }
        }
    }

    [JsonIgnore]
    public DateTimeOffset NextCheck
    {
        get => _nextCheck;
        set => SetField(ref _nextCheck, value);
    }

    [JsonIgnore]
    public string CheckIntervalText => $"{CheckIntervalMinutes} min";

    [JsonIgnore]
    public string EnabledText => Enabled ? "Yes" : "No";

    [JsonIgnore]
    public string LastCheckedText => LastChecked?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
