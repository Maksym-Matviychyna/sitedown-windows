using System.IO;
using System.Text;
using System.Text.Json;

namespace SiteDownWindows.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiteDown");
        Directory.CreateDirectory(folder);

        // Encrypted settings file.
        _settingsPath = Path.Combine(folder, "settings.dat");

        // Old plain-text settings file used by older versions. It is only used for migration.
        _legacySettingsPath = Path.Combine(folder, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var encryptedBytes = await File.ReadAllBytesAsync(_settingsPath);
                var jsonBytes = DpapiSettingsProtector.Unprotect(encryptedBytes);
                var json = Encoding.UTF8.GetString(jsonBytes);

                return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
            catch
            {
                // If encrypted settings are invalid or were edited manually, ignore them safely.
                return new AppSettings();
            }
        }

        // Migration from old plain-text settings.json.
        if (File.Exists(_legacySettingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_legacySettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();

                await SaveAsync(settings);
                TryDeleteLegacySettings();

                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        return new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var folder = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = DpapiSettingsProtector.Protect(jsonBytes);

        await File.WriteAllBytesAsync(_settingsPath, encryptedBytes);

        // Remove old readable file after successful encrypted save.
        TryDeleteLegacySettings();
    }

    private void TryDeleteLegacySettings()
    {
        try
        {
            if (File.Exists(_legacySettingsPath))
            {
                File.Delete(_legacySettingsPath);
            }
        }
        catch
        {
            // Not critical. The app will no longer use the legacy file when settings.dat exists.
        }
    }
}
