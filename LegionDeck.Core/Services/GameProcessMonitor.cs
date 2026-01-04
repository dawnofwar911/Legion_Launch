using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class GameProcessMonitor : IDisposable
{
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _isRunning = false;
    private System.Threading.Timer? _pollingTimer;
    private HashSet<int> _runningProcessIds = new();

    public event Action<string, int, string?>? ProcessStarted;
    public event Action<string, int>? ProcessStopped;

    private static readonly object _logLock = new();

    public GameProcessMonitor()
    {
    }

    public void Start()
    {
        if (_isRunning) return;

        Log("Starting Game Process Monitor...");

        try
        {
            // Watch for process start (Requires Admin usually)
            _startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += (s, e) =>
            {
                string processName = e.NewEvent.Properties["ProcessName"].Value.ToString() ?? "Unknown";
                int processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
                string? executablePath = GetProcessPath(processId);
                
                Log($"[WMI] Process Started: {processName} (PID: {processId}, Path: {executablePath ?? "Unknown"})");
                ProcessStarted?.Invoke(processName, processId, executablePath);
            };

            // Watch for process stop (Requires Admin usually)
            _stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += (s, e) =>
            {
                string processName = e.NewEvent.Properties["ProcessName"].Value.ToString() ?? "Unknown";
                int processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
                Log($"[WMI] Process Stopped: {processName} (PID: {processId})");
                ProcessStopped?.Invoke(processName, processId);
            };

            _startWatcher.Start();
            _stopWatcher.Start();
            _isRunning = true;
            Log("WMI Monitor active.");
        }
        catch (Exception ex)
        {
            Log($"WMI start failed (Permissions?): {ex.Message}. Falling back to polling.");
            StartPolling();
        }
    }

    private void StartPolling()
    {
        // Snapshot current processes
        _runningProcessIds = Process.GetProcesses().Select(p => p.Id).ToHashSet();
        
        // Poll every 2 seconds
        _pollingTimer = new System.Threading.Timer(PollProcesses, null, 2000, 2000);
        _isRunning = true;
        Log("Polling Monitor active (2s interval).");
    }

    private void PollProcesses(object? state)
    {
        try
        {
            var currentProcesses = Process.GetProcesses();
            var currentIds = currentProcesses.Select(p => p.Id).ToHashSet();

            // Started
            foreach (var p in currentProcesses)
            {
                if (!_runningProcessIds.Contains(p.Id))
                {
                    string name = p.ProcessName;
                    int id = p.Id;
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    
                    Log($"[Poll] Process Started: {name} (PID: {id}, Path: {path ?? "Unknown"})");
                    ProcessStarted?.Invoke(name, id, path);
                }
            }

            // Stopped
            foreach (var id in _runningProcessIds)
            {
                if (!currentIds.Contains(id))
                {
                    Log($"[Poll] Process Stopped (PID: {id})");
                    ProcessStopped?.Invoke("Unknown", id);
                }
            }

            _runningProcessIds = currentIds;
        }
        catch (Exception ex)
        {
            Log($"Polling error: {ex.Message}");
        }
    }

    private string? GetProcessPath(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}");
            using var results = searcher.Get();
            foreach (var obj in results)
            {
                return obj["ExecutablePath"]?.ToString();
            }
        }
        catch { }
        return null;
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _startWatcher?.Stop();
        _stopWatcher?.Stop();
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _isRunning = false;
        Log("Process Monitor stopped.");
    }

    private void Log(string message)
    {
        lock (_logLock)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck");
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, "startup.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [GameProcessMonitor] {message}\n");
            }
            catch { }
        }
    }

    public void Dispose()
    {
        Stop();
        _startWatcher?.Dispose();
        _stopWatcher?.Dispose();
        _pollingTimer?.Dispose();
    }
}
