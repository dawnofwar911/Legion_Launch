using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LegionDeck.Core.Models;

namespace LegionDeck.Core.Services;

public class GameDetectionService
{
    private readonly GameProcessMonitor _monitor;
    private readonly LibraryCacheService _cacheService;
    private readonly ConfigService _configService;
    private List<LocalLibraryService.InstalledGame> _allGames = new();
        private Dictionary<int, LocalLibraryService.InstalledGame> _runningGames = new();
        
        // Store original settings to restore
        private string? _originalResolution; 
        
        // Mapping of common process names to game names
        private static readonly Dictionary<string, string> _executableOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "bf6", "Battlefield 6" },
            { "FallGuys_client_game", "Fall Guys" },
            { "FortniteClient-Win64-Shipping", "Fortnite" },
            { "Overwatch", "Overwatch 2" },
            { "Wow", "World of Warcraft" },
            { "WowClassic", "World of Warcraft" },
            { "Diablo III", "Diablo III" },
            { "Diablo IV", "Diablo IV" },
            { "StarCraftII", "StarCraft II" }
        };
    
            public event Action<LocalLibraryService.InstalledGame>? GameStarted;
            public event Action<LocalLibraryService.InstalledGame>? GameStopped;
        
            public GameDetectionService(GameProcessMonitor monitor, LibraryCacheService cacheService, ConfigService configService)
            {
                _monitor = monitor;
                _cacheService = cacheService;
                _configService = configService;
                _monitor.ProcessStarted += OnProcessStarted;
                _monitor.ProcessStopped += OnProcessStopped;
                
                _ = LoadGamesAsync();
            }
        
            private async Task LoadGamesAsync()
            {
                var sources = new[] { "Steam", "Xbox", "Ubisoft", "EA", "Epic", "Battle.net" };
                var all = new List<LocalLibraryService.InstalledGame>();
                foreach (var source in sources)
                {
                    all.AddRange(await _cacheService.LoadLibraryAsync(source));
                }
                _allGames = all;
                Log($"GameDetectionService: Loaded {_allGames.Count} games from cache for matching.");
            }
        
            private void OnProcessStarted(string processName, int pid, string? executablePath)
            {
                // Try to match process to a game
                var game = MatchProcessToGame(processName, pid, executablePath);
                if (game != null)
                {
                    bool isNewGame = !_runningGames.Values.Any(g => g.Id == game.Id);
                    _runningGames[pid] = game;
                    Log($"[GAME DETECTED] {game.Name} (Source: {game.Source}, PID: {pid})");
                    
                    if (isNewGame)
                    {
                        // Get user preference for this game
                        var profile = _configService.GetProfile(game.Id);
                        Log($"Applying per-game power mode: {(LenovoPowerService.PowerMode)profile.PowerMode}");
                        LenovoPowerService.SetPowerMode((LenovoPowerService.PowerMode)profile.PowerMode);
                        
                        // 2. Apply Resolution/Refresh Rate
                        if (!string.IsNullOrEmpty(profile.Resolution))
                        {
                            // Capture current state if not already captured
                            if (_runningGames.Count == 1) 
                            {
                                _originalResolution = DisplayService.GetCurrentMode();
                                Log($"Saved original display mode: {_originalResolution}");
                            }
        
                            var parts = profile.Resolution.Split(',');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                            {
                                Log($"Applying per-game resolution: {w}x{h} @ {profile.RefreshRate ?? 60}Hz");
                                DisplayService.SetDisplayMode(w, h, profile.RefreshRate ?? 60);
                            }
                        }
        
                        // Lock Windows key for handheld gaming
                        LenovoPowerService.SetWinKeyLock(true);
                        GameStarted?.Invoke(game);
                    }
                }
            }    
        private void OnProcessStopped(string processName, int pid)
        {
            if (_runningGames.Remove(pid, out var game))
            {
                Log($"[GAME STOPPED] {game.Name} (PID: {pid})");
                
                // Revert settings if no other games are running
                if (!_runningGames.Any())
                {
                    Log("No games running. Restoring defaults.");
                    LenovoPowerService.SetPowerMode(LenovoPowerService.PowerMode.Balanced);
                    LenovoPowerService.SetWinKeyLock(false);
                    
                    // Restore Display
                    if (!string.IsNullOrEmpty(_originalResolution))
                    {
                        Log($"Restoring display mode: {_originalResolution}");
                        // Parse "1920x1200 @ 144Hz"
                        try 
                        {
                            var parts = _originalResolution.Split('@');
                            var resParts = parts[0].Trim().Split('x');
                            var hzPart = parts[1].Trim().Replace("Hz", "");
                            
                            if (int.TryParse(resParts[0], out int w) && 
                                int.TryParse(resParts[1], out int h) && 
                                int.TryParse(hzPart, out int hz))
                            {
                                DisplayService.SetDisplayMode(w, h, hz);
                            }
                        } 
                        catch { Log("Failed to parse original resolution string."); }
                        _originalResolution = null;
                    }
                }
                
                GameStopped?.Invoke(game);
            }
        }
    private LocalLibraryService.InstalledGame? MatchProcessToGame(string processName, int pid, string? executablePath)
    {
        var nameNoExt = Path.GetFileNameWithoutExtension(processName);
        if (IsSystemProcess(nameNoExt)) return null;

        // 1. Try hardcoded overrides
        if (_executableOverrides.TryGetValue(nameNoExt, out var overridenName))
        {
            var match = _allGames.FirstOrDefault(g => g.Name.Equals(overridenName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // 2. Try matching by Executable Path (Most reliable for non-Steam and Xbox)
        if (!string.IsNullOrEmpty(executablePath))
        {
            // A. Check if path is within a known install path
            var matchByPath = _allGames.FirstOrDefault(g => 
                !string.IsNullOrEmpty(g.InstallPath) && 
                executablePath.StartsWith(g.InstallPath, StringComparison.OrdinalIgnoreCase));
            
            if (matchByPath != null) return matchByPath;

            // B. Heuristic: Check if the path contains the game name (useful for WindowsApps/XboxGames)
            if (executablePath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) || 
                executablePath.Contains("XboxGames", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains("EA Games", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains("Epic Games", StringComparison.OrdinalIgnoreCase))
            {
                var matchByNameInPath = _allGames.FirstOrDefault(g => 
                {
                    var cleanName = CleanForSearch(g.Name);
                    return !string.IsNullOrEmpty(cleanName) && executablePath.Contains(cleanName, StringComparison.OrdinalIgnoreCase);
                });
                
                if (matchByNameInPath != null) return matchByNameInPath;
            }
        }

        // 3. Try matching by Name
        var cleanProcessName = CleanForSearch(nameNoExt);
        var matchByName = _allGames.FirstOrDefault(g => 
        {
            var cleanGameName = CleanForSearch(g.Name);
            return cleanGameName.Equals(cleanProcessName, StringComparison.OrdinalIgnoreCase) ||
                   cleanGameName.Contains(cleanProcessName, StringComparison.OrdinalIgnoreCase) ||
                   cleanProcessName.Contains(cleanGameName, StringComparison.OrdinalIgnoreCase);
        });

        if (matchByName != null) return matchByName;
        
        return null;
    }

    private string CleanForSearch(string name)
    {
        return name.Replace(" ", "").Replace(":", "").Replace("™", "").Replace("®", "").Replace("-", "").Trim().ToLowerInvariant();
    }

    private bool IsSystemProcess(string name)
    {
        var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "msedge", "chrome", "svchost", "runtimebroker", "searchhost", 
            "taskmgr", "cmd", "powershell", "conhost", "dllhost", "LegionDeck.GUI"
        };
        return systemProcesses.Contains(name);
    }

    private void Log(string message)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [GameDetectionService] {message}\n");
        }
        catch {{ }}
    }
}
