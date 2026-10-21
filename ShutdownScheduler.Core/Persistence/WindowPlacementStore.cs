using System.IO;
using System.Text.Json;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Persistence;

public sealed class WindowPlacementStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShutdownScheduler",
        "window-placement.json");

    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path));
        }
        catch
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        // Losing the last window position is harmless, so a failed write must never block closing the app.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(placement, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
