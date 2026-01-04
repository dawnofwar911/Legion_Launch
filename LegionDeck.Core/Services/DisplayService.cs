using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

namespace LegionDeck.Core.Services;

public class DisplayService
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll")]
    public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int CDS_UPDATEREGISTRY = 0x01;
    public const int CDS_TEST = 0x02;
    public const int DISP_CHANGE_SUCCESSFUL = 0;

    public static string GetCurrentMode()
    {
        DEVMODE vDevMode = new DEVMODE();
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref vDevMode))
        {
            return $"{vDevMode.dmPelsWidth}x{vDevMode.dmPelsHeight} @ {vDevMode.dmDisplayFrequency}Hz";
        }
        return "Unknown";
    }

    public static bool SetDisplayMode(int width, int height, int frequency)
    {
        DEVMODE vDevMode = new DEVMODE();
        
        // Find the matching mode
        int modeNum = 0;
        while (EnumDisplaySettings(null, modeNum++, ref vDevMode))
        {
            if (vDevMode.dmPelsWidth == width &&
                vDevMode.dmPelsHeight == height &&
                vDevMode.dmDisplayFrequency == frequency)
            {
                int result = ChangeDisplaySettings(ref vDevMode, CDS_UPDATEREGISTRY);
                return result == DISP_CHANGE_SUCCESSFUL;
            }
        }
        return false;
    }

    public static List<string> GetSupportedModes()
    {
        var modes = new HashSet<string>();
        DEVMODE vDevMode = new DEVMODE();
        int modeNum = 0;
        
        while (EnumDisplaySettings(null, modeNum++, ref vDevMode))
        {
            // Filter for common aspect ratios or specific Legion Go resolutions to avoid clutter
            if (vDevMode.dmPelsWidth >= 800) 
            {
                modes.Add($"{vDevMode.dmPelsWidth}x{vDevMode.dmPelsHeight} @ {vDevMode.dmDisplayFrequency}Hz");
            }
        }
        return modes.OrderByDescending(x => x).ToList();
    }
}
