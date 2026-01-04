using System;
using System.Management;
using System.Windows.Forms;

namespace LegionDeck.Core.Services;

public class LenovoPowerService
{
    private const string WmiNamespace = @"root\WMI";
    private const string GameZoneDataClass = "LENOVO_GAMEZONE_DATA";

    public enum PowerMode
    {
        Quiet = 1,
        Balanced = 2,
        Performance = 3
    }

    public static PowerMode GetCurrentPowerMode()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("GetSmartFanMode", null, null);
                if (outParams != null)
                {
                    return (PowerMode)Convert.ToInt32(outParams["Data"]);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to get power mode: {ex.Message}");
        }
        return PowerMode.Balanced; // Default fallback
    }

    public static bool SetPowerMode(PowerMode mode)
    {
        // Safety Check: Avoid power mode changes on low battery to prevent potential freezes/instability
        if (GetBatteryPercentage() < 30)
        {
            Log($"Power mode change to {mode} blocked: Battery below 30%.");
            return false;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("SetSmartFanMode");
                inParams["Data"] = (int)mode;

                obj.InvokeMethod("SetSmartFanMode", inParams, null);
                Log($"Power mode set to {mode}.");
                return true; 
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to set power mode: {ex.Message}");
        }
        return false;
    }

    public static bool GetFanBoostStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("GetFanCoolingStatus", null, null);
                if (outParams != null)
                {
                    // Usually 1 = On, 0 = Off
                    return Convert.ToInt32(outParams["Data"]) == 1;
                }
            }
        }
        catch (Exception ex) { Log($"Failed to get Fan Boost status: {ex.Message}"); }
        return false;
    }

    public static bool GetTrackpadStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("GetTPStatus", null, null);
                if (outParams != null)
                {
                    // Usually 1 = Enabled, 0 = Disabled
                    return Convert.ToInt32(outParams["Data"]) == 1;
                }
            }
        }
        catch (Exception ex) { Log($"Failed to get Trackpad status: {ex.Message}"); }
        return true; // Default to enabled
    }

    public static bool SetWinKeyLock(bool locked)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("SetWinKeyStatus");
                inParams["Data"] = locked ? 1 : 0; // Usually 1 = Locked (Disabled)
                obj.InvokeMethod("SetWinKeyStatus", inParams, null);
                Log($"WinKey Lock set to: {locked}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set WinKey Lock: {ex.Message}"); }
        return false;
    }

    public static bool SetTrackpadStatus(bool enabled)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("SetTPStatus");
                inParams["Data"] = enabled ? 1 : 0;
                obj.InvokeMethod("SetTPStatus", inParams, null);
                Log($"Trackpad enabled: {enabled}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set Trackpad status: {ex.Message}"); }
        return false;
    }

    public static bool SetFanBoost(bool enabled)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("SetFanCooling");
                inParams["Data"] = enabled ? 1 : 0; // 1 = Max/Cooling on, 0 = Normal
                obj.InvokeMethod("SetFanCooling", inParams, null);
                Log($"Fan Boost set to: {enabled}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set Fan Boost: {ex.Message}"); }
        return false;
    }

    public static bool SetKeyboardLight(int data)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("SetKeyboardLight");
                inParams["Data"] = (uint)data;
                obj.InvokeMethod("SetKeyboardLight", inParams, null);
                Log($"Keyboard Light set to: {data}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set Keyboard Light: {ex.Message}"); }
        return false;
    }

    public static bool SetIGPUMode(int mode)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {GameZoneDataClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("SetIGPUModeStatus");
                inParams["mode"] = (uint)mode;
                obj.InvokeMethod("SetIGPUModeStatus", inParams, null);
                Log($"iGPU Mode set to: {mode}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set iGPU Mode: {ex.Message}"); }
        return false;
    }

    public static bool SetLighting(byte id, byte state, byte brightness)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM LENOVO_LIGHTING_METHOD");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("Set_Lighting_Current_Status");
                inParams["Lighting_ID"] = id;
                inParams["Current_State_Type"] = state;
                inParams["Current_Brightness_Level"] = brightness;

                obj.InvokeMethod("Set_Lighting_Current_Status", inParams, null);
                Log($"Lighting set: ID={id}, State={state}, Brightness={brightness}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set lighting: {ex.Message}"); }
        return false;
    }

    public static bool SetFanTable(byte tableId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM LENOVO_FAN_METHOD");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("Fan_Set_Table");
                inParams["FanTable"] = tableId; // Usually 0=Auto, 1=Full?
                obj.InvokeMethod("Fan_Set_Table", inParams, null);
                Log($"Fan Table set to: {tableId}");
                return true;
            }
        }
        catch (Exception ex) { Log($"Failed to set fan table: {ex.Message}"); }
        return false;
    }

    public static int GetBatteryPercentage()
    {
        try
        {
            var powerStatus = SystemInformation.PowerStatus;
            return (int)(powerStatus.BatteryLifePercent * 100);
        }
        catch {{ return 100; }}
    }

    private static void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LenovoPowerService] {message}\n");
        }
        catch {{ }}
    }
}