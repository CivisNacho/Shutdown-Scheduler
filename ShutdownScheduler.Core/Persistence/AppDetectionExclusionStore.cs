using System.IO;

namespace Shutdown_Scheduler.Persistence;

public sealed class AppDetectionExclusionStore
{
    private readonly string _path = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "ShutdownScheduler.Core",
        "Persistence",
        "ExcludedExecutables.txt");

    public IReadOnlySet<string> LoadExecutableNames()
    {
        var normalizedPath = Path.GetFullPath(_path);
        EnsureFileExists();

        try
        {
            return File.ReadAllLines(normalizedPath)
                .Select(TryNormalizeExecutableName)
                .Where(line => line is not null)
                .Select(line => line!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return DefaultExecutableNames
                .Select(TryNormalizeExecutableName)
                .Where(line => line is not null)
                .Select(line => line!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string PathOnDisk => Path.GetFullPath(_path);

    private void EnsureFileExists()
    {
        var normalizedPath = Path.GetFullPath(_path);
        if (File.Exists(normalizedPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        File.WriteAllLines(normalizedPath, BuildDefaultFileContents());
    }

    private static IEnumerable<string> BuildDefaultFileContents()
    {
        yield return "# One executable name per line.";
        yield return "# Lines starting with # are comments.";
        yield return "# Write only the executable file name, including .exe.";
        yield return "# Examples:";
        yield return "# updater.exe";
        yield return "# driverhelper.exe";
        yield return "# graphicscontrolpanel.exe";
        yield return "# GoogleUpdate.exe";
        yield return "# BraveUpdate.exe";
        yield return "# SDXHelper.exe";
        yield return "# igfxTray.exe";
        yield return "# RadeonSoftware.exe";
        yield return "# pwsh.exe";
        yield return string.Empty;

        foreach (var executable in DefaultExecutableNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            yield return executable;
        }
    }

    private static string? TryNormalizeExecutableName(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.StartsWith('#'))
        {
            return null;
        }

        var withoutInlineComment = trimmed.Split('#', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(withoutInlineComment))
        {
            return null;
        }

        var unquoted = withoutInlineComment.Trim().Trim('"');
        var fileName = Path.GetFileName(unquoted);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".exe";
        }

        return fileName.ToLowerInvariant();
    }

    private static readonly string[] DefaultExecutableNames =
    {
        "AMDSoftware.exe",
        "AMDRSServ.exe",
        "ArcControl.exe",
        "ArcControlAssist.exe",
        "atieclxx.exe",
        "atiesrxx.exe",
        "BraveUpdate.exe",
        "cncmd.exe",
        "GoogleUpdate.exe",
        "igfxCUIServiceN.exe",
        "igfxEM.exe",
        "igfxHK.exe",
        "igfxTray.exe",
        "IntelCpHDCPSvc.exe",
        "IntelGraphicsSoftware.exe",
        "msedgewebview2.exe",
        "RadeonSoftware.exe",
        "SDXHelper.exe",
        "pwsh.exe",
        "vgtray.exe",
    };
}
