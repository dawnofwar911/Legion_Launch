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
            Log($"Ubisoft scan finished. Total games so far: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Ubisoft games: {ex.Message}");
        }

        try
        {
            Log("Starting Epic game scan");
            allGames.AddRange(GetInstalledEpicGames());
            Log($"Epic scan finished. Total games so far: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Epic games: {ex.Message}");
        }

        try
        {
            Log("Starting EA game scan");
            allGames.AddRange(GetInstalledEaGames());
            Log($"EA scan finished. Total games so far: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning EA games: {ex.Message}");
        }

        try
        {
            Log("Starting Xbox game scan");
            allGames.AddRange(GetInstalledXboxGames());
            Log($"Xbox scan finished. Total games so far: {allGames.Count}");
        }
        catch (Exception ex)
        {
            Log($"Error scanning Xbox games: {ex.Message}");
        }
        
        return allGames;
    }

    public List<InstalledGame> GetInstalledXboxGames()
    {
        var games = new List<InstalledGame>();
        try
        {
            // Xbox games are usually in C:\XboxGames or C:\Program Files\WindowsApps
            // Scanning C:\XboxGames is easier and less permission-intensive
            string xboxGamesPath = @"C:\XboxGames";
            if (Directory.Exists(xboxGamesPath))
            {
                foreach (var gameDir in Directory.GetDirectories(xboxGamesPath))
                {
                    try
                    {
                        string configPath = Path.Combine(gameDir, "Content", "MicrosoftGame.config");
                        if (!File.Exists(configPath)) configPath = Path.Combine(gameDir, "MicrosoftGame.config");

                        if (File.Exists(configPath))
                        {
                            var xml = File.ReadAllText(configPath);
                            var storeIdMatch = System.Text.RegularExpressions.Regex.Match(xml, "<StoreId>(.*?)</StoreId>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(xml, "DefaultDisplayName=\"(.*?)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            // Look for <Executable Name="..." Id="..." />
                            var exeIdMatch = System.Text.RegularExpressions.Regex.Match(xml, "<Executable.*?Id=\"(.*?)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                            if (storeIdMatch.Success)
                            {
                                string storeId = storeIdMatch.Groups[1].Value.Trim();
                                string name = nameMatch.Success ? nameMatch.Groups[1].Value : Path.GetFileName(gameDir);
                                string? exeId = exeIdMatch.Success ? exeIdMatch.Groups[1].Value : null;

                                string launchUri = !string.IsNullOrEmpty(exeId) 
                                    ? $"msgamelaunch://shortcutLaunch/?ProductId={storeId}&Exe={exeId}"
                                    : $"ms-windows-store://pdp/?ProductId={storeId}";

                                games.Add(new InstalledGame
                                {
                                    Id = storeId,
                                    Name = name,
                                    Source = "Xbox",
                                    InstallPath = gameDir,
                                    LaunchUri = launchUri
                                });
                            }
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
        var searchRoots = new[] { Registry.LocalMachine, Registry.CurrentUser };
        var registryPaths = new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };

        foreach (var root in searchRoots)
        {
            foreach (var regPath in registryPaths)
            {
                try
                {
                    using var key = root.OpenSubKey(regPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            string? publisher = subKey.GetValue("Publisher") as string;
                            if (publisher != null && (publisher.Contains("Electronic Arts") || publisher.Contains("EA")))
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

                                    if (contentId != null && !games.Any(g => g.Id == contentId))
                                    {
                                        games.Add(new InstalledGame
                                        {
                                            Id = contentId,
                                            Name = name,
                                            Source = "EA",
                                            InstallPath = installDir,
                                            LaunchUri = $"origin://launchgame/{contentId}"
                                        });
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        return games;
    }

    public List<InstalledGame> GetInstalledUbisoftGames()
    {
        var games = new List<InstalledGame>();
        var paths = new[] { @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", @"SOFTWARE\Ubisoft\Launcher\Installs" };

        foreach (var keyPath in paths)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            if (subKey != null)
                            {
                                string? name = subKey.GetValue("Name") as string 
                                            ?? subKey.GetValue("DisplayName") as string
                                            ?? subKey.GetValue("GameName") as string;

                                string? installDir = subKey.GetValue("InstallDir") as string;
                                
                                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(installDir))
                                {
                                    name = Path.GetFileName(installDir.TrimEnd('\\', '/'));
                                }

                                if (string.IsNullOrEmpty(name)) name = "Ubisoft Game " + subKeyName;

                                if (!games.Any(g => g.Id == subKeyName))
                                {
                                    games.Add(new InstalledGame
                                    {
                                        Id = subKeyName,
                                        Name = name,
                                        Source = "Ubisoft",
                                        InstallPath = installDir ?? string.Empty,
                                        LaunchUri = "uplay://launch/" + subKeyName + "/0"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
        return games;
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

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [Core.LibraryService] {message}\n");
        }
        catch { }
    }
}
