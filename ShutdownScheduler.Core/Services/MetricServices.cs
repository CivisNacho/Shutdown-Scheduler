using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler.Services;

public sealed class CpuMonitorService : IDisposable
{
    private readonly PerformanceCounter? _counter;
    private readonly Queue<(DateTime Time, double Value)> _samples = new();
    private readonly Dictionary<string, ProcessCpuSampler> _processSamplers = new(StringComparer.OrdinalIgnoreCase);

    public CpuMonitorService()
    {
        try
        {
            _counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _counter.NextValue();
        }
        catch
        {
            _counter = null;
        }
    }

    public bool IsAvailable => _counter is not null;

    public double SampleAverage()
    {
        if (_counter is null) return 0;
        try
        {
            AddSample(Math.Clamp(_counter.NextValue(), 0, 100));
        }
        catch
        {
            return 0;
        }

        return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.Value);
    }

    public double SampleAverageForProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return 0;
        }

        if (!_processSamplers.TryGetValue(processName, out var sampler))
        {
            sampler = new ProcessCpuSampler(processName);
            _processSamplers[processName] = sampler;
        }

        return sampler.SampleAverage();
    }

    private void AddSample(double value)
    {
        var now = DateTime.Now;
        _samples.Enqueue((now, value));
        while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(60))
        {
            _samples.Dequeue();
        }
    }

    public void Dispose() => _counter?.Dispose();

    private sealed class ProcessCpuSampler
    {
        private readonly string _processName;
        private readonly Queue<(DateTime Time, double Value)> _samples = new();
        private readonly Dictionary<int, ProcessCpuSnapshot> _snapshots = new();

        public ProcessCpuSampler(string processName)
        {
            _processName = processName;
        }

        public double SampleAverage()
        {
            var now = DateTime.Now;
            double totalPercent = 0;
            var seenProcessIds = new HashSet<int>();

            foreach (var process in Process.GetProcessesByName(_processName))
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    seenProcessIds.Add(process.Id);
                    var totalProcessorTime = process.TotalProcessorTime;
                    if (_snapshots.TryGetValue(process.Id, out var previous))
                    {
                        var elapsed = Math.Max((now - previous.Time).TotalSeconds, 0.1);
                        var cpuDelta = Math.Max(0, (totalProcessorTime - previous.TotalProcessorTime).TotalSeconds);
                        totalPercent += cpuDelta / (elapsed * Environment.ProcessorCount) * 100;
                    }

                    _snapshots[process.Id] = new ProcessCpuSnapshot(now, totalProcessorTime);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            foreach (var processId in _snapshots.Keys.Except(seenProcessIds).ToList())
            {
                _snapshots.Remove(processId);
            }

            AddSample(Math.Clamp(totalPercent, 0, 100));
            return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.Value);
        }

        private void AddSample(double value)
        {
            var now = DateTime.Now;
            _samples.Enqueue((now, value));
            while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(60))
            {
                _samples.Dequeue();
            }
        }
    }

    private readonly record struct ProcessCpuSnapshot(DateTime Time, TimeSpan TotalProcessorTime);
}

public sealed class NetworkMonitorService
{
    private readonly Queue<(DateTime Time, double BytesPerSecond)> _samples = new();
    private long _lastBytes;
    private DateTime _lastTime = DateTime.MinValue;
    private readonly Dictionary<string, ProcessNetworkSampler> _processSamplers = new(StringComparer.OrdinalIgnoreCase);

    public double SampleAverageBytesPerSecond()
    {
        var now = DateTime.Now;
        var bytes = GetTotalBytes();
        if (_lastTime != DateTime.MinValue)
        {
            var seconds = Math.Max((now - _lastTime).TotalSeconds, 0.1);
            var delta = Math.Max(0, bytes - _lastBytes);
            AddSample(delta / seconds);
        }

        _lastBytes = bytes;
        _lastTime = now;
        return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.BytesPerSecond);
    }

    public double SampleAverageBytesPerSecondForProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return 0;
        }

        if (!_processSamplers.TryGetValue(processName, out var sampler))
        {
            sampler = new ProcessNetworkSampler(processName);
            _processSamplers[processName] = sampler;
        }

        return sampler.SampleAverageBytesPerSecond();
    }

    private static long GetTotalBytes()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Sum(adapter =>
            {
                var stats = adapter.GetIPv4Statistics();
                return stats.BytesReceived + stats.BytesSent;
            });
    }

    private void AddSample(double bytesPerSecond)
    {
        var now = DateTime.Now;
        _samples.Enqueue((now, bytesPerSecond));
        while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(60))
        {
            _samples.Dequeue();
        }
    }

    private sealed class ProcessNetworkSampler
    {
        private readonly string _processName;
        private readonly Queue<(DateTime Time, double BytesPerSecond)> _samples = new();
        private readonly Dictionary<int, long> _snapshots = new();
        private DateTime _lastTime = DateTime.MinValue;

        public ProcessNetworkSampler(string processName)
        {
            _processName = processName;
        }

        public double SampleAverageBytesPerSecond()
        {
            var now = DateTime.Now;
            long totalDelta = 0;
            var seenProcessIds = new HashSet<int>();

            foreach (var process in Process.GetProcessesByName(_processName))
            {
                try
                {
                    if (process.HasExited || !TryGetIoCounters(process.Handle, out var ioCounters))
                    {
                        continue;
                    }

                    seenProcessIds.Add(process.Id);
                    var totalBytes = checked((long)Math.Min(
                        long.MaxValue,
                        ioCounters.ReadTransferCount + ioCounters.WriteTransferCount + ioCounters.OtherTransferCount));

                    if (_snapshots.TryGetValue(process.Id, out var previousTotalBytes))
                    {
                        totalDelta += Math.Max(0, totalBytes - previousTotalBytes);
                    }

                    _snapshots[process.Id] = totalBytes;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            foreach (var processId in _snapshots.Keys.Except(seenProcessIds).ToList())
            {
                _snapshots.Remove(processId);
            }

            if (_lastTime != DateTime.MinValue)
            {
                var seconds = Math.Max((now - _lastTime).TotalSeconds, 0.1);
                AddSample(totalDelta / seconds);
            }

            _lastTime = now;
            return _samples.Count == 0 ? 0 : _samples.Average(sample => sample.BytesPerSecond);
        }

        private void AddSample(double bytesPerSecond)
        {
            var now = DateTime.Now;
            _samples.Enqueue((now, bytesPerSecond));
            while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(60))
            {
                _samples.Dequeue();
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters ioCounters);

    internal static bool TryGetIoCounters(IntPtr processHandle, out IoCounters ioCounters)
    {
        try
        {
            return GetProcessIoCounters(processHandle, out ioCounters);
        }
        catch
        {
            ioCounters = default;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
}

// Watches one app (every process with the given name) on a background timer and tracks since when it has been idle.
// Sampling starts with the schedule, so idle time before the target time already counts.
public sealed class AppIdleMonitor : IDisposable
{
    // Below all three averages the app counts as idle. I/O covers network and disk together:
    // Windows exposes no per-process network counter without kernel tracing.
    private const double CpuIdlePercent = 2;
    private const double IoIdleBytesPerSecond = 50 * 1024;
    private const double GpuIdlePercent = 3;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SmoothingWindow = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Queue<(DateTime Time, double Cpu, double Io, double Gpu)> _window = new();
    private readonly Dictionary<int, (TimeSpan Cpu, ulong Io)> _previousProcesses = new();
    private Dictionary<string, CounterSample> _previousGpu = new();
    private Timer? _timer;
    private string _processName = string.Empty;
    private DateTime _previousTime;
    private DateTime? _idleSince;
    private bool _gpuAvailable = true;
    private int _sampling;
    private AppActivitySnapshot _current = AppActivitySnapshot.Unknown;

    public AppActivitySnapshot Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Start(string processName)
    {
        lock (_gate)
        {
            StopCore();
            _processName = processName;
            _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, SampleInterval);
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    public void Dispose() => Stop();

    private void StopCore()
    {
        _timer?.Dispose();
        _timer = null;
        _window.Clear();
        _previousProcesses.Clear();
        _previousGpu = new();
        _previousTime = default;
        _idleSince = null;
        _current = AppActivitySnapshot.Unknown;
    }

    private void Sample()
    {
        // A slow sample (the GPU category can take a while) must not overlap the next tick.
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try
        {
            string processName;
            lock (_gate)
            {
                if (_timer is null) return;
                processName = _processName;
            }

            var now = DateTime.UtcNow;
            var processes = Process.GetProcessesByName(processName);
            var readable = new Dictionary<int, (TimeSpan Cpu, ulong Io)>();
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && NetworkMonitorService.TryGetIoCounters(process.Handle, out var io))
                    {
                        readable[process.Id] = (process.TotalProcessorTime, io.ReadTransferCount + io.WriteTransferCount + io.OtherTransferCount);
                    }
                }
                catch
                {
                    // Access denied (elevated app) or the process just exited.
                }
                finally
                {
                    process.Dispose();
                }
            }

            var gpuPercent = SampleGpu(readable.Keys.ToHashSet());
            lock (_gate)
            {
                if (_timer is null) return;
                _current = Update(now, processes.Length > 0, readable, gpuPercent);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    private AppActivitySnapshot Update(DateTime now, bool isRunning, Dictionary<int, (TimeSpan Cpu, ulong Io)> readable, double gpuPercent)
    {
        if (!isRunning)
        {
            // A closed app has nothing left to do, so it counts as idle from the moment it disappears.
            _previousProcesses.Clear();
            _window.Clear();
            _idleSince ??= now;
            return new AppActivitySnapshot(false, true, 0, 0, 0, _idleSince);
        }

        if (readable.Count == 0)
        {
            _idleSince = null;
            return new AppActivitySnapshot(true, false, 0, 0, 0, null);
        }

        var hasPrevious = _previousTime != default;
        var elapsed = Math.Max((now - _previousTime).TotalSeconds, 0.1);
        double cpuSeconds = 0;
        double ioBytes = 0;
        foreach (var (processId, sample) in readable)
        {
            if (_previousProcesses.TryGetValue(processId, out var previous))
            {
                cpuSeconds += Math.Max(0, (sample.Cpu - previous.Cpu).TotalSeconds);
                ioBytes += sample.Io >= previous.Io ? sample.Io - previous.Io : 0;
            }
        }

        _previousProcesses.Clear();
        foreach (var (processId, sample) in readable) _previousProcesses[processId] = sample;
        _previousTime = now;
        if (!hasPrevious)
        {
            return _current with { IsRunning = true, IsReadable = true };
        }

        _window.Enqueue((now, cpuSeconds / (elapsed * Environment.ProcessorCount) * 100, ioBytes / elapsed, gpuPercent));
        while (now - _window.Peek().Time > SmoothingWindow) _window.Dequeue();

        var cpu = _window.Average(sample => sample.Cpu);
        var ioRate = _window.Average(sample => sample.Io);
        var gpu = _window.Average(sample => sample.Gpu);
        var isIdle = cpu < CpuIdlePercent && ioRate < IoIdleBytesPerSecond && gpu < GpuIdlePercent;
        _idleSince = isIdle ? _idleSince ?? now : null;
        return new AppActivitySnapshot(true, true, cpu, ioRate, gpu, _idleSince);
    }

    // Per-process GPU use as Task Manager shows it: utilization summed per engine type, highest engine type wins.
    private double SampleGpu(HashSet<int> processIds)
    {
        if (!_gpuAvailable || processIds.Count == 0) return 0;
        try
        {
            var utilization = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            if (utilization is null) return 0;

            var current = new Dictionary<string, CounterSample>();
            var perEngineType = new Dictionary<string, double>();
            foreach (InstanceData instance in utilization.Values)
            {
                // Instance names look like "pid_1234_luid_0x..._phys_0_eng_0_engtype_3D".
                var name = instance.InstanceName;
                if (!TryParseProcessId(name, out var processId) || !processIds.Contains(processId)) continue;

                current[name] = instance.Sample;
                if (_previousGpu.TryGetValue(name, out var previous))
                {
                    var engineTypeStart = name.LastIndexOf("engtype_", StringComparison.Ordinal);
                    var engineType = engineTypeStart >= 0 ? name[(engineTypeStart + 8)..] : name;
                    perEngineType[engineType] = perEngineType.GetValueOrDefault(engineType) + CounterSample.Calculate(previous, instance.Sample);
                }
            }

            _previousGpu = current;
            return perEngineType.Count == 0 ? 0 : Math.Clamp(perEngineType.Values.Max(), 0, 100);
        }
        catch
        {
            // No GPU counters on this machine (old drivers, some VMs): judge idleness by CPU and I/O only.
            _gpuAvailable = false;
            return 0;
        }
    }

    private static bool TryParseProcessId(string instanceName, out int processId)
    {
        processId = 0;
        if (!instanceName.StartsWith("pid_", StringComparison.Ordinal)) return false;
        var end = instanceName.IndexOf('_', 4);
        return end > 4 && int.TryParse(instanceName.AsSpan(4, end - 4), out processId);
    }
}

public sealed class BatteryMonitorService
{
    public bool IsBatteryAvailable => TryGetBatteryPercentage(out _);

    public bool TryGetBatteryPercentage(out double percentage)
    {
        percentage = 0;

        try
        {
            if (!GetSystemPowerStatus(out var powerStatus))
            {
                return false;
            }

            if (powerStatus.BatteryFlag == BatteryFlagNoSystemBattery ||
                powerStatus.BatteryLifePercent is BatteryLifeUnknown or > 100)
            {
                return false;
            }

            percentage = Math.Clamp((double)powerStatus.BatteryLifePercent, 0, 100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private const byte BatteryFlagNoSystemBattery = 128;
    private const byte BatteryLifeUnknown = 255;
}

public sealed class ConditionEvaluationService
{
    private readonly CpuMonitorService _cpu;
    private readonly NetworkMonitorService _network;
    private readonly BatteryMonitorService _battery;
    private readonly AppIdleMonitor _appIdle;
    private readonly LocalizationService _loc;

    public ConditionEvaluationService(
        CpuMonitorService cpu,
        NetworkMonitorService network,
        BatteryMonitorService battery,
        AppIdleMonitor appIdle,
        LocalizationService loc)
    {
        _cpu = cpu;
        _network = network;
        _battery = battery;
        _appIdle = appIdle;
        _loc = loc;
    }

    public void BeginSchedule(Models.ScheduleRequest request)
    {
        if (request.AppIdleConditionEnabled)
        {
            _appIdle.Start(request.AppIdleProcessName);
        }
        else
        {
            _appIdle.Stop();
        }
    }

    public void EndSchedule() => _appIdle.Stop();

    public ConditionEvaluationResult Evaluate(Models.ScheduleRequest request)
    {
        var useAppScopedMetrics = request.Action == PowerAction.CloseApp &&
            !string.IsNullOrWhiteSpace(request.TargetProcessName);

        var cpuAverage = useAppScopedMetrics
            ? _cpu.SampleAverageForProcess(request.TargetProcessName)
            : _cpu.SampleAverage();
        var networkAverage = useAppScopedMetrics
            ? _network.SampleAverageBytesPerSecondForProcess(request.TargetProcessName)
            : _network.SampleAverageBytesPerSecond();
        var hasBatteryPercentage = _battery.TryGetBatteryPercentage(out var batteryPercent);

        if (request.NetworkConditionEnabled && networkAverage >= request.NetworkThresholdBytesPerSecond)
        {
            return new ConditionEvaluationResult
            {
                IsSatisfied = false,
                CpuAverage = cpuAverage,
                NetworkAverageBytesPerSecond = networkAverage,
                BatteryPercent = batteryPercent,
                Message = $"{_loc.T("NetworkWaiting")} {_loc.T("CurrentAverage")}: {FormatBytes(networkAverage)}. {_loc.T("RequiredBelow")} {FormatBytes(request.NetworkThresholdBytesPerSecond)}."
            };
        }

        if (request.CpuConditionEnabled && cpuAverage >= request.CpuThresholdPercent)
        {
            return new ConditionEvaluationResult
            {
                IsSatisfied = false,
                CpuAverage = cpuAverage,
                NetworkAverageBytesPerSecond = networkAverage,
                BatteryPercent = batteryPercent,
                Message = $"{_loc.T("CpuWaiting")} {_loc.T("CurrentAverage")}: {cpuAverage:0.0}%. {_loc.T("RequiredBelow")} {request.CpuThresholdPercent:0.0}%."
            };
        }

        if (request.BatteryConditionEnabled)
        {
            if (!hasBatteryPercentage)
            {
                return new ConditionEvaluationResult
                {
                    IsSatisfied = false,
                    CpuAverage = cpuAverage,
                    NetworkAverageBytesPerSecond = networkAverage,
                    BatteryPercent = 0,
                    Message = _loc.T("BatteryUnavailable")
                };
            }

            if (batteryPercent >= request.BatteryThresholdPercent)
            {
                return new ConditionEvaluationResult
                {
                    IsSatisfied = false,
                    CpuAverage = cpuAverage,
                    NetworkAverageBytesPerSecond = networkAverage,
                    BatteryPercent = batteryPercent,
                    Message = $"{_loc.T("BatteryWaiting")} {_loc.T("CurrentAverage")}: {batteryPercent:0.0}%. {_loc.T("RequiredBelow")} {request.BatteryThresholdPercent:0.0}%."
                };
            }
        }

        if (request.AppIdleConditionEnabled && GetAppIdleWaitMessage(request) is { } appIdleMessage)
        {
            return new ConditionEvaluationResult
            {
                IsSatisfied = false,
                CpuAverage = cpuAverage,
                NetworkAverageBytesPerSecond = networkAverage,
                BatteryPercent = batteryPercent,
                Message = appIdleMessage
            };
        }

        return new ConditionEvaluationResult
        {
            IsSatisfied = true,
            CpuAverage = cpuAverage,
            NetworkAverageBytesPerSecond = networkAverage,
            BatteryPercent = batteryPercent
        };
    }

    // Null when the app has been idle (or closed) for the required time.
    private string? GetAppIdleWaitMessage(Models.ScheduleRequest request)
    {
        var activity = _appIdle.Current;
        var label = string.IsNullOrWhiteSpace(request.AppIdleProcessLabel) ? request.AppIdleProcessName : request.AppIdleProcessLabel;
        if (!activity.IsReadable)
        {
            return string.Format(_loc.T("AppIdleUnreadable"), label);
        }

        if (activity.IdleSinceUtc is not { } idleSince)
        {
            var usage = $"CPU {activity.CpuPercent:0.0}%, {_loc.T("IoShort")} {FormatBytes(activity.IoBytesPerSecond)}, GPU {activity.GpuPercent:0}%";
            return string.Format(_loc.T("AppIdleWaitingActive"), label, usage);
        }

        var idleFor = DateTime.UtcNow - idleSince;
        var required = TimeSpan.FromMinutes(request.AppIdleMinutes);
        return idleFor >= required
            ? null
            : string.Format(_loc.T("AppIdleWaitingCounting"), label, RemainingTimeFormatter.FormatHoursMinutesSeconds(idleFor), request.AppIdleMinutes);
    }

    public static string FormatBytes(double bytesPerSecond)
        => bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / 1024 / 1024:0.00} MB/s"
            : $"{bytesPerSecond / 1024:0.0} KB/s";
}
