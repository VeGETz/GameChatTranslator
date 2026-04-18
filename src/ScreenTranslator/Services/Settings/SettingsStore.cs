using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenTranslator.Services.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private AppSettings _current;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenTranslator");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // Corrupt file — fall back to defaults rather than crashing.
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_current, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    /// <summary>DPAPI-protect a plaintext API key and store it on the current settings.</summary>
    public void SetGoogleCloudApiKey(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            _current.GoogleCloudApiKeyProtected = null;
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        _current.GoogleCloudApiKeyProtected = Convert.ToBase64String(protectedBytes);
    }

    public string? GetGoogleCloudApiKey() => Unprotect(_current.GoogleCloudApiKeyProtected);

    public void SetLlmApiKey(string? plaintext)
    {
        _current.LlmApiKeyProtected = Protect(plaintext);
    }

    public string? GetLlmApiKey() => Unprotect(_current.LlmApiKeyProtected);

    private static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return null;
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
