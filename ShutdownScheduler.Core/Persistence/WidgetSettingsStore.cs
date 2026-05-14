using System.IO;
using System.Text.Json;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Persistence;

public sealed class WidgetSettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShutdownScheduler",
        "widget-settings.json");

    public WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new WidgetSettings();
            return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(_path)) ?? new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public void Save(WidgetSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
