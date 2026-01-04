using System;
using System.Management;
using System.Collections.Generic;
using System.Linq;

namespace LegionDeck.Core.Utilities;

public class WmiExplorer
{
    public static List<string> Explore()
    {
        var results = new List<string>();
        results.Add("Exploring WMI namespaces for Lenovo-related classes...");

        // Common Lenovo WMI namespaces to check (Forward slashes used to avoid escaping issues)
        List<string> namespaces = new List<string>
        {
            "root/WMI",
            "root/CIMV2",
            "root/Lenovo", 
            "root/Lenovo/Power"
        };

        foreach (string ns in namespaces)
        {
            results.Add($"\n--- Checking Namespace: {ns} ---");
            try
            {
                ManagementScope scope = new ManagementScope(ns.Replace("/", "\\"));
                scope.Connect();

                // Get all classes in the namespace
                ObjectQuery query = new ObjectQuery("SELECT * FROM Meta_Class");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementClass wmiClass in searcher.Get())
                {
                    string className = wmiClass["__CLASS"].ToString() ?? "Unknown";
                    
                    // Filter for classes that might be related to Lenovo or power management
                    if (className.Contains("Lenovo", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Thermal", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("TDP", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("ACPI", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Led", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Rgb", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Ring", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Light", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add($"  Class: {className}");

                        // List methods
                        try 
                        {
                            foreach (MethodData method in wmiClass.Methods)
                            {
                                string paramInfo = "";
                                if (method.InParameters != null)
                                {
                                    var props = new List<string>();
                                    foreach (var p in method.InParameters.Properties) props.Add($"{p.Name}({p.Type})");
                                    paramInfo = " [Params: " + string.Join(", ", props) + "]";
                                }
                                
                                results.Add($"    Method: {method.Name}{paramInfo}");
                                if (method.Name.StartsWith("Set", StringComparison.OrdinalIgnoreCase))
                                {
                                    results.Add($"    *** POTENTIAL SETTER: {method.Name} ***");
                                }
                            }
                        } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add($"  Error accessing namespace {ns}: {ex.Message}");
            }
        }
        return results;
    }

    public static string ReadProductInfo()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("GetProductInfo", null, null);
                return $"Product Info: {outParams?["Data"] ?? "Unknown"}";
            }
            return "LENOVO_GAMEZONE_DATA not found.";
        }
        catch (Exception ex) { return $"Error reading product info: {ex.Message}"; }
    }

    public static string ReadPanelStatus()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_PANEL_METHOD");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var latency = obj.InvokeMethod("Panel_Get_Low_Latency_Mode", null, null)?["Data"];
                var status = obj.InvokeMethod("Panel_Get_Status", null, null)?["Data"];
                var displayMode = obj.InvokeMethod("Panel_Get_Display_Mode", null, null)?["Data"];
                var fps = obj.InvokeMethod("Panel_Get_Game_Aid_FPS", null, null)?["Data"];
                
                return $"Status: {status}, LowLatency: {latency}, DisplayMode: {displayMode}, FPS: {fps}";
            }
            return "LENOVO_PANEL_METHOD not found.";
        }
        catch (Exception ex) { return $"Error reading panel status: {ex.Message}"; }
    }

    public static string ReadThermalMode()
    {
        var mode = LegionDeck.Core.Services.LenovoPowerService.GetCurrentPowerMode();
        return $"Current Power Mode: {mode}";
    }

    public static string ReadSmartFanMode()
    {
        // Now just a legacy alias for current power mode
        return ReadThermalMode();
    }

    public static string ReadBattery()
    {
        return $"Battery Level: {LegionDeck.Core.Services.LenovoPowerService.GetBatteryPercentage()}%";
    }

    public static string ReadTemperatures()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var cpu = obj.InvokeMethod("GetCPUTemp", null, null)?["Data"];
                var gpu = obj.InvokeMethod("GetGPUTemp", null, null)?["Data"];
                return $"CPU Temp: {cpu}°C, GPU Temp: {gpu}°C";
            }
            return "LENOVO_GAMEZONE_DATA not found.";
        }
        catch (Exception ex) { return $"Error reading temperatures: {ex.Message}"; }
    }

    public static string ReadOsStatus()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var winKey = obj.InvokeMethod("GetWinKeyStatus", null, null)?["Data"];
                var tp = obj.InvokeMethod("GetTPStatus", null, null)?["Data"];
                return $"WinKey Locked: {winKey}, Trackpad Enabled: {tp}";
            }
            return "LENOVO_GAMEZONE_DATA not found.";
        }
        catch (Exception ex) { return $"Error reading OS status: {ex.Message}"; }
    }

    public static string ReadThermalZoneTemp()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM MSAcpi_ThermalZoneTemperature");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                // CurrentTemperature is in tenths of Kelvin
                var tempRaw = Convert.ToDouble(obj["CurrentTemperature"]);
                var celsius = (tempRaw / 10.0) - 273.15;
                return $"ACPI Thermal Zone: {celsius:F1}°C";
            }
            return "MSAcpi_ThermalZoneTemperature not found.";
        }
        catch (Exception ex) { return $"Error reading ACPI temp: {ex.Message}"; }
    }

    public static string CheckFanCoolingSupport()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("IsSupportFanCooling", null, null);
                return $"IsSupportFanCooling: {outParams?["Data"] ?? "Unknown"}";
            }
            return "LENOVO_GAMEZONE_DATA not found.";
        }
        catch (Exception ex) { return $"Error checking fan support: {ex.Message}"; }
    }

    public static string ReadFanCoolingStatus()
    {
        try
        {
            ManagementScope scope = new ManagementScope(@"root\WMI");
            scope.Connect();
            ObjectQuery query = new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("GetFanCoolingStatus", null, null);
                return $"GetFanCoolingStatus: {outParams?["Data"] ?? "Unknown"}";
            }
            return "LENOVO_GAMEZONE_DATA not found.";
        }
        catch (Exception ex) { return $"Error reading fan status: {ex.Message}"; }
    }

    public static string SetPanelCrosshair(int mode)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_PANEL_METHOD");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("Panel_Set_Game_Aid_Sight_Mode");
                inParams["mode"] = (uint)mode;
                obj.InvokeMethod("Panel_Set_Game_Aid_Sight_Mode", inParams, null);
                return $"SetPanelCrosshair({mode}) called.";
            }
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
        return "LENOVO_PANEL_METHOD not found.";
    }

    public static string SetPanelGamut(int mode)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_PANEL_METHOD");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("Panel_Set_Gamut_Switch");
                inParams["mode"] = (uint)mode;
                obj.InvokeMethod("Panel_Set_Gamut_Switch", inParams, null);
                return $"SetPanelGamut({mode}) called.";
            }
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
        return "LENOVO_PANEL_METHOD not found.";
    }

    public static string SetLowLatency(int mode)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_PANEL_METHOD");
            foreach (ManagementObject obj in searcher.Get())
            {
                var inParams = obj.GetMethodParameters("Panel_Set_Low_Latency_Mode");
                inParams["mode"] = (uint)mode;
                obj.InvokeMethod("Panel_Set_Low_Latency_Mode", inParams, null);
                return $"SetLowLatency({mode}) called.";
            }
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
        return "LENOVO_PANEL_METHOD not found.";
    }

    public static string SetThermalMode(int mode)
    {
        bool success = LegionDeck.Core.Services.LenovoPowerService.SetPowerMode((LegionDeck.Core.Services.LenovoPowerService.PowerMode)mode);
        return $"SetPowerMode({mode}) success: {success}";
    }

    public static string SetSmartFanMode(int mode)
    {
        return SetThermalMode(mode);
    }

    public static string SetFanBoost(bool enabled)
    {
        bool success = LegionDeck.Core.Services.LenovoPowerService.SetFanBoost(enabled);
        return $"SetFanBoost({enabled}) success: {success}";
    }

    public static string SetTrackpadStatus(bool enabled)
    {
        bool success = LegionDeck.Core.Services.LenovoPowerService.SetTrackpadStatus(enabled);
        return $"SetTrackpadStatus({enabled}) success: {success}";
    }

    public static string SetLighting(int id, int state, int brightness)
    {
        bool success = LegionDeck.Core.Services.LenovoPowerService.SetLighting((byte)id, (byte)state, (byte)brightness);
        return $"SetLighting(ID={id}, State={state}, Bri={brightness}) success: {success}";
    }

    public static string SetFanTable(int tableId)
    {
        bool success = LegionDeck.Core.Services.LenovoPowerService.SetFanTable((byte)tableId);
        return $"SetFanTable({tableId}) success: {success}";
    }

    public static string SetFeature(int id, int data)
    {
        return "SetFeatureValue removed in favor of SetPowerMode.";
    }
}
