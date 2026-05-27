using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SiteDownWindows.Models;

namespace SiteDownWindows.Services;

public sealed record PairingResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<SiteMonitorItem> Sites { get; init; } = new();
    public int? LatestVersionCode { get; init; }
}

public sealed class PairingService
{
    private readonly HttpClient _httpClient;

    // Same token config endpoint as the Android app.
    private static readonly Uri[] PairingEndpoints =
    {
        new("https://sitedown.app/api/config/")
    };

    public PairingService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SiteDown-Windows/1.0");
    }

    public async Task<PairingResult> PairAsync(string pairKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pairKey))
        {
            return new PairingResult { Success = false, Message = "Token is empty." };
        }

        Exception? lastException = null;
        string lastResponse = string.Empty;

        foreach (var endpoint in PairingEndpoints)
        {
            try
            {
                var requestJson = JsonSerializer.Serialize(new
                {
                    token = pairKey.Trim()
                });

                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                lastResponse = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var parsed = ParsePairingResponse(lastResponse);
                if (parsed.Success)
                {
                    return parsed with { Message = string.IsNullOrWhiteSpace(parsed.Message) ? $"Token accepted using {endpoint}" : parsed.Message };
                }

                if (!string.IsNullOrWhiteSpace(parsed.Message))
                {
                    return parsed;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
            }
        }

        if (lastException != null)
        {
            return new PairingResult { Success = false, Message = lastException.Message };
        }

        return new PairingResult
        {
            Success = false,
            Message = string.IsNullOrWhiteSpace(lastResponse)
                ? "Could not save the token. Check the endpoint path on your website."
                : "Token failed. Server response: " + TrimForLog(lastResponse)
        };
    }

    private static PairingResult ParsePairingResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PairingResult { Success = false, Message = "Empty response from server." };
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var success = GetBool(root, "success")
                          || string.Equals(GetString(root, "status"), "success", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(GetString(root, "result"), "success", StringComparison.OrdinalIgnoreCase);

            var message = GetString(root, "message")
                          ?? GetString(root, "msg")
                          ?? GetString(root, "error")
                          ?? string.Empty;

            var sites = FindSites(root);
            var latestVersionCode = FindLatestVersionCode(root);

            return new PairingResult
            {
                Success = success,
                Message = message,
                Sites = sites,
                LatestVersionCode = latestVersionCode
            };
        }
        catch (JsonException)
        {
            var lowered = json.Trim().ToLowerInvariant();
            if (lowered == "success" || lowered.Contains("\"success\"") || lowered.Contains("success"))
            {
                return new PairingResult { Success = true, Message = "Token saved successfully." };
            }

            return new PairingResult
            {
                Success = false,
                Message = "Server did not return valid JSON: " + TrimForLog(json)
            };
        }
    }

    private static int? FindLatestVersionCode(JsonElement root)
    {
        var directValue = GetNullableInt(root, "windows_latest_version_code", "windowsLatestVersionCode");
        if (directValue.HasValue)
        {
            return directValue.Value;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var nestedValue = GetNullableInt(data, "windows_latest_version_code", "windowsLatestVersionCode");
            if (nestedValue.HasValue)
            {
                return nestedValue.Value;
            }
        }

        return null;
    }

    private static List<SiteMonitorItem> FindSites(JsonElement root)
    {
        if (TryGetArray(root, out var array, "sites", "websites", "monitored_sites"))
        {
            return ParseSitesArray(array);
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (TryGetArray(data, out var nestedArray, "sites", "websites", "monitored_sites"))
            {
                return ParseSitesArray(nestedArray);
            }
        }

        return new List<SiteMonitorItem>();
    }

    private static List<SiteMonitorItem> ParseSitesArray(JsonElement array)
    {
        var result = new List<SiteMonitorItem>();

        if (array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var site in array.EnumerateArray())
        {
            if (site.ValueKind != JsonValueKind.Object) continue;

            var name = GetString(site, "name") ?? GetString(site, "Name") ?? "Website";
            var url = GetString(site, "url") ?? GetString(site, "URL") ?? string.Empty;
            var keyword = GetString(site, "expected_keyword")
                          ?? GetString(site, "expectedKeyword")
                          ?? GetString(site, "keyword")
                          ?? GetString(site, "wKeyword")
                          ?? string.Empty;
            var interval = GetInt(site, 3, "check_interval", "checkInterval", "check_interval_minutes", "interval", "wInterval");
            var enabled = GetEnabled(site);

            if (string.IsNullOrWhiteSpace(url)) continue;

            result.Add(new SiteMonitorItem
            {
                Name = name,
                Url = NormalizeUrl(url),
                ExpectedKeyword = keyword,
                CheckIntervalMinutes = Math.Max(1, interval),
                Enabled = enabled,
                LastStatus = "Waiting",
                NextCheck = DateTimeOffset.MinValue
            });
        }

        return result;
    }

    private static bool GetEnabled(JsonElement element)
    {
        var status = GetString(element, "status") ?? GetString(element, "Status");
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status.Equals("ON", StringComparison.OrdinalIgnoreCase)
                   || status.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || status.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || status.Equals("active", StringComparison.OrdinalIgnoreCase)
                   || status.Equals("enabled", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var name in new[] { "enabled", "active", "is_enabled" })
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var property))
            {
                if (property.ValueKind == JsonValueKind.True) return true;
                if (property.ValueKind == JsonValueKind.False) return false;
                if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue)) return intValue == 1;
                if (property.ValueKind == JsonValueKind.String)
                {
                    var value = property.GetString();
                    return value == "1" || value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                }
            }
        }

        return true;
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        array = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyCaseInsensitive(element, name, out var property)) continue;

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        return null;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        if (!TryGetPropertyCaseInsensitive(element, name, out var property)) return false;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => property.TryGetInt32(out var number) && number == 1,
            JsonValueKind.String => IsTrueString(property.GetString()),
            _ => false
        };
    }

    private static bool IsTrueString(string? value)
    {
        return value != null &&
               (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("success", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static int? GetNullableInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyCaseInsensitive(element, name, out var property)) continue;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static int GetInt(JsonElement element, int defaultValue, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyCaseInsensitive(element, name, out var property)) continue;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return defaultValue;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in element.EnumerateObject())
            {
                if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    property = item.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "https://" + trimmed;
    }

    private static string TrimForLog(string value)
    {
        var trimmed = value.Trim().Replace("\r", " ").Replace("\n", " ");
        return trimmed.Length <= 180 ? trimmed : trimmed[..180] + "...";
    }
}
