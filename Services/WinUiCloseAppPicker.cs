using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Services;
using Shutdown_Scheduler.ViewModels;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Shutdown_Scheduler_WinUI.Services;

public sealed class CloseAppPickerItem : ObservableObject
{
    private ImageSource? _icon;
    private bool _isIconLoaded;

    public CloseAppPickerItem(string processName, string label, string executablePath)
    {
        ProcessName = processName;
        Label = label;
        ExecutablePath = executablePath;
    }

    public string ProcessName { get; }
    public string Label { get; }
    public string ExecutablePath { get; }

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool IsIconLoaded
    {
        get => _isIconLoaded;
        set => SetProperty(ref _isIconLoaded, value);
    }
}

public sealed class CloseAppPickerState : ObservableObject
{
    private CloseAppPickerItem? _selectedItem;
    private bool _isLoading;

    public CloseAppPickerState(LocalizationService loc)
    {
        AppToCloseLabel = loc.T("AppToClose");
        RefreshAppsLabel = loc.T("RefreshApps");
        NoAppsFoundLabel = loc.T("NoAppsFound");
        LoadingAppsLabel = loc.T("LoadingApps");
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<CloseAppPickerItem> Items { get; } = new();
    public string AppToCloseLabel { get; }
    public string RefreshAppsLabel { get; }
    public string NoAppsFoundLabel { get; }
    public string LoadingAppsLabel { get; }

    public CloseAppPickerItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                SelectedItemChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(HasNoItems));
            }
        }
    }

    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => !IsLoading && Items.Count == 0;

    public event EventHandler<CloseAppPickerItem?>? SelectedItemChanged;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
    }
}

public sealed class WinUiAppIconCache
{
    private readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _pending = new(StringComparer.OrdinalIgnoreCase);

    public async Task<byte[]?> GetOrLoadAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var bytes = await GetOrLoadBytesAsync(executablePath).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return bytes is { Length: > 0 } ? bytes : null;
    }

    private async Task<byte[]?> GetOrLoadBytesAsync(string executablePath)
    {
        if (_cache.TryGetValue(executablePath, out var cached))
        {
            return cached;
        }

        var lazyLoader = _pending.GetOrAdd(
            executablePath,
            static path => new Lazy<Task<byte[]?>>(() => LoadBytesAsync(path), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var bytes = await lazyLoader.Value.ConfigureAwait(false);
            _cache[executablePath] = bytes;
            return bytes;
        }
        finally
        {
            _pending.TryRemove(executablePath, out _);
        }
    }

    private static async Task<byte[]?> LoadBytesAsync(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath))
            {
                return null;
            }

            var file = await StorageFile.GetFileFromPathAsync(executablePath);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            await using var stream = thumbnail.AsStreamForRead();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
