using System;
using System.IO;
using System.Management;
using NAudio.CoreAudioApi;

namespace LegionDeck.Core.Services;

public class SystemControlService
{
    private const string WmiNamespace = @"root\WMI";

    #region Brightness
    private static ManagementEventWatcher? _brightnessWatcher;
    public static event EventHandler<int>? OnBrightnessChanged;

    public static void StartBrightnessMonitoring()
    {
        if (_brightnessWatcher != null) return;

        try
        {
            var scope = new ManagementScope(WmiNamespace);
            var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            _brightnessWatcher = new ManagementEventWatcher(scope, query);
            _brightnessWatcher.EventArrived += BrightnessWatcher_EventArrived;
            _brightnessWatcher.Start();
        }
        catch (Exception ex)
        {
            Log($"Failed to start brightness monitoring: {ex.Message}");
        }
    }

    private static void BrightnessWatcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        // Event received, fetch the new canonical value
        int newBrightness = GetBrightness();
        if (newBrightness != -1)
        {
            OnBrightnessChanged?.Invoke(null, newBrightness);
        }
    }

    public static void SetBrightness(int brightness)
    {
        if (brightness < 0) brightness = 0;
        if (brightness > 100) brightness = 100;

        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)brightness });
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to set brightness: {ex.Message}");
        }
    }

    public static int GetBrightness()
    {
        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightness"));
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                return Convert.ToInt32(obj["CurrentBrightness"]);
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to get brightness: {ex.Message}");
        }
        return -1;
    }
    #endregion

    #region Volume (NAudio)

    private static MMDeviceEnumerator? _enumerator;
    private static MMDevice? _device;
    public static event EventHandler<float>? OnVolumeChanged;

    public static void StartVolumeMonitoring()
    {
        if (_device != null) return; // Already monitoring

        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
        }
        catch (Exception ex)
        {
            Log($"Failed to start volume monitoring: {ex.Message}");
        }
    }

    private static void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
    {
        OnVolumeChanged?.Invoke(null, data.MasterVolume * 100f);
    }

    public static float GetMasterVolume()
    {
        try
        {
            // If monitoring is active, use the existing device to avoid re-creating it constantly
            if (_device != null)
            {
                return _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
            }

            using var tempEnumerator = new MMDeviceEnumerator();
            using var tempDevice = tempEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return tempDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        }
        catch (Exception ex)
        {
            Log($"GetMasterVolume exception: {ex.Message}");
            return 50; 
        }
    }

    public static void SetMasterVolume(float level)
    {
        try
        {
            if (level < 0) level = 0;
            if (level > 100) level = 100;

            // If monitoring is active, use the existing device
            if (_device != null)
            {
                 _device.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100.0f;
                 return;
            }

            using var tempEnumerator = new MMDeviceEnumerator();
            using var tempDevice = tempEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            tempDevice.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100.0f;
        }
        catch (Exception ex)
        {
            Log($"SetMasterVolume exception: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SystemControlService] {message}\n");
        }
        catch {{ }}
    }
    #endregion
}