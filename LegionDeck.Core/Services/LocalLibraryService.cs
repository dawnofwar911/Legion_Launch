using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Diagnostics;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

[SupportedOSPlatform("windows")]
public class LocalLibraryService
{
    public class InstalledGame
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // Steam, Xbox, Ubisoft
        public string? LaunchUri { get; set; }
    }

    public async Task LaunchGameAsync(InstalledGame game)
    {
        string? uri = game.LaunchUri;
        
        if (string.IsNullOrEmpty(uri))
        {
            uri = game.Source.ToLower() switch
            {
                "steam" => $"steam://run/{game.Id}",
                "ubisoft" => $"uplay://launch/{game.Id}/0",
                _ => null
            };
        }

        if (!string.IsNullOrEmpty(uri))
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        await Task.CompletedTask;
    }

    public async Task<List<InstalledGame>> GetInstalledGamesAsync()
    {
        return await Task.Run(() => GetInstalledGames());
    }

    public List<InstalledGame> GetInstalledGames()
    {
        var allGames = new List<InstalledGame>();
        allGames.AddRange(GetInstalledSteamGames());
        allGames.AddRange(GetInstalledUbisoftGames());
        return allGames;
    }

    public List<InstalledGame> GetInstalledSteamGames()
    {
        var games = new List<InstalledGame>();
        string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steamPath)) return games;

        steamPath = steamPath.Replace("/", "\\");
        var steamAppsPath = Path.Combine(steamPath, "steamapps");

        if (Directory.Exists(steamAppsPath))
        {
            games.AddRange(ScanSteamDir(steamAppsPath));
        }

        var libraryFoldersVdf = Path.Combine(steamAppsPath, "libraryfolders.vdf");
        if (File.Exists(libraryFoldersVdf))
        {
            try
            {
                var lines = File.ReadAllLines(libraryFoldersVdf);
                foreach (var line in lines)
                {
                    if (line.Contains("\"path\""))
                    {
                        var parts = line.Split('"');
                        if (parts.Length >= 4)
                        {
                            var extraPath = parts[3].Replace("\\\\", "\\");
                            var extraSteamApps = Path.Combine(extraPath, "steamapps");
                            if (Directory.Exists(extraSteamApps) && extraSteamApps.ToLower() != steamAppsPath.ToLower())
                            {
                                games.AddRange(ScanSteamDir(extraSteamApps));
                            }
                        }
                    }
                }
            }
                catch { }
        }

        return games;
    }

    private List<InstalledGame> ScanSteamDir(string path)
    {
        var games = new List<InstalledGame>();
        if (!Directory.Exists(path)) return games;
        
        var manifestFiles = Directory.GetFiles(path, "appmanifest_*.acf");

        foreach (var file in manifestFiles)
        {
            try
            {
                var content = File.ReadAllLines(file);
                string? name = null;
                string? appId = null;

                foreach (var line in content)
                {
                    if (line.Contains("\"name\"")) name = line.Split('"')[3];
                    if (line.Contains("\"appid\"")) appId = line.Split('"')[3];
                }

                if (name != null && appId != null && appId != "228980") // Filter out redistributables
                {
                    games.Add(new InstalledGame
                    {
                        Id = appId,
                        Name = name,
                        Source = "Steam",
                        InstallPath = Path.Combine(path, "common", name),
                        LaunchUri = $"steam://run/{appId}"
                    });
                }
            }
                catch { }
        }
        return games;
    }

    public List<InstalledGame> GetInstalledUbisoftGames()
    {
        var games = new List<InstalledGame>();
        try
        {
            string keyPath = "SOFTWARE\\WOW6432Node\\Ubisoft\\Launcher\\Installs";
            using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            if (subKey != null)
                            {
                                string? installDir = subKey.GetValue("InstallDir") as string;
                                if (!string.IsNullOrEmpty(installDir))
                                {
                                    string gameName = Path.GetFileName(installDir.TrimEnd('\\')) ?? "Ubisoft Game";
                                    games.Add(new InstalledGame
                                    {
                                        Id = subKeyName,
                                        Name = gameName,
                                        Source = "Ubisoft",
                                        InstallPath = installDir,
                                        LaunchUri = "uplay://launch/" + subKeyName + "/0"
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }
            catch { }
        return games;
    }
}
