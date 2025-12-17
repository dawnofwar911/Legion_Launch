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
                "epic" => $"com.epicgames.launcher://apps/{game.Id}?action=launch&silent=true",
                "ea" => $"origin://launchgame/{game.Id}",
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
        try
        {
            Log("Starting Steam game scan");
            allGames.AddRange(GetInstalledSteamGames());
            Log($"Steam scan finished. Total games: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Steam games: {ex.Message}");
        }

        try
        {
            Log("Starting Ubisoft game scan");
            allGames.AddRange(GetInstalledUbisoftGames());
            Log($"Ubisoft scan finished. Total games: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Ubisoft games: {ex.Message}");
        }

        try
        {
            Log("Starting Epic game scan");
            allGames.AddRange(GetInstalledEpicGames());
            Log($"Epic scan finished. Total games: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Epic games: {ex.Message}");
        }

        try
        {
            Log("Starting EA game scan");
            allGames.AddRange(GetInstalledEaGames());
            Log($"EA scan finished. Total games: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning EA games: {ex.Message}");
        }
        
        return allGames;
    }

    public List<InstalledGame> GetInstalledEpicGames()
    {
        var games = new List<InstalledGame>();
        try
        {
            string manifestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifestPath))
            {
                foreach (var file in Directory.GetFiles(manifestPath, "*.item"))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        using var doc = System.Text.Json.JsonDocument.Parse(content);
                        var root = doc.RootElement;
                        string? name = root.GetProperty("DisplayName").GetString();
                        string? appId = root.GetProperty("AppName").GetString();
                        if (name != null && appId != null)
                        {
                            games.Add(new InstalledGame
                            {
                                Id = appId,
                                Name = name,
                                Source = "Epic",
                                LaunchUri = $"com.epicgames.launcher://apps/{appId}?action=launch&silent=true"
                            });
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return games;
    }

    public List<InstalledGame> GetInstalledEaGames()
    {
        var games = new List<InstalledGame>();
        try
        {
            // EA usually stores info in C:\ProgramData\EA Desktop\Metadata or via AppData
            // But NexusHub scans the installation folder for __Installer/installerdata.xml
            // A more reliable way is checking the registry for EA Desktop installs
            string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                    {
                        string? publisher = subKey.GetValue("Publisher") as string;
                        if (publisher == "Electronic Arts")
                        {
                            string? name = subKey.GetValue("DisplayName") as string;
                            string? installDir = subKey.GetValue("InstallLocation") as string;
                            if (name != null && !string.IsNullOrEmpty(installDir))
                            {
                                // Try to find ContentID in __Installer/installerdata.xml
                                string installerXml = Path.Combine(installDir, "__Installer", "installerdata.xml");
                                string? contentId = null;
                                if (File.Exists(installerXml))
                                {
                                    var xml = File.ReadAllText(installerXml);
                                    var match = System.Text.RegularExpressions.Regex.Match(xml, "<contentID>(.*?)</contentID>");
                                    if (match.Success) contentId = match.Groups[1].Value;
                                }

                                games.Add(new InstalledGame
                                {
                                    Id = contentId ?? subKeyName,
                                    Name = name,
                                    Source = "EA",
                                    InstallPath = installDir,
                                    LaunchUri = contentId != null ? $"origin://launchgame/{contentId}" : null
                                });
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return games;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [Core.LibraryService] {message}\n");
        }
        catch { }
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
