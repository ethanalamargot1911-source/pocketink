using System.IO;
using System.Text.Json;
using PocketInk.Core.Models;

namespace PocketInk.Host.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON under %LOCALAPPDATA%\PocketInk (spec #89).
/// Falls back to defaults on a missing or corrupt file rather than crashing startup; a failed
/// save is likewise swallowed since losing a settings write should never take down the host.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketInk", "settings.json");

    private readonly string _filePath;

    public SettingsStore() : this(DefaultFilePath)
    {
    }

    public SettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
