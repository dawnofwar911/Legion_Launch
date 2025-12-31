using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class LibraryUpdateService
{
    private static bool _hasUpdated = false;
    private static readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SubscriptionLibraryService _subService = new();
    private readonly SteamLibraryService _steamService;
    private readonly LibraryCacheService _cacheService = new();
    private readonly LocalLibraryService _localService = new();

    public LibraryUpdateService()
    {
        _steamService = new SteamLibraryService(new ConfigService());
    }

    public static void ResetUpdateFlag()
    {
        _hasUpdated = false;
    }

    public async Task UpdateAllAsync(string? source = null)
    {
        if (source == null && _hasUpdated) return;
        
        if (!await _syncLock.WaitAsync(10000)) 
        {
            Log("Sync already in progress or timed out. Skipping call.");
            return;
        }

        try 
        {
            Log($"UpdateAllAsync starting (Source: {source ?? "ALL"})...");
            var localGames = await _localService.GetInstalledGamesAsync();

            // 1. Steam
            if (source == null || source.Equals("Steam", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var items = await _steamService.GetOwnedGamesAsync();
                    var list = items.Select(i => new LocalLibraryService.InstalledGame { Id = i.AppId.ToString(), Name = i.Name, Source = "Steam", IsInstalled = false }).ToList();
                    _localService.UpdateInstallationStatus(list, localGames);
                    await _cacheService.SaveLibraryAsync("Steam", list);
                } catch (Exception ex) { Log($"Steam Sync Error: {ex.Message}"); }
            }

            // 2. Xbox
            if (source == null || source.Equals("Xbox", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var xboxService = new XboxLibraryService();
                    var catalog = await xboxService.GetGamePassGamesAsync();
                    var list = catalog.Select(item => new LocalLibraryService.InstalledGame { Id = item.SteamAppId ?? item.Name, Name = item.Name, Source = "Xbox", IsInstalled = false }).ToList();
                    _localService.UpdateInstallationStatus(list, localGames);
                    await _cacheService.SaveLibraryAsync("Xbox", list);
                } catch (Exception ex) { Log($"Xbox Sync Error: {ex.Message}"); }
            }

            // 3. Ubisoft
            if (source == null || source.Equals("Ubisoft", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var items = await _subService.GetUbisoftPlusGamesAsync();
                    var list = items.Select(i => new LocalLibraryService.InstalledGame { Id = i.Name, Name = i.Name, Source = "Ubisoft", IsInstalled = false }).ToList();
                    _localService.UpdateInstallationStatus(list, localGames);
                    await _cacheService.SaveLibraryAsync("Ubisoft", list);
                } catch (Exception ex) { Log($"Ubisoft Sync Error: {ex.Message}"); }
            }

            // 4. EA
            if (source == null || source.StartsWith("EA", StringComparison.OrdinalIgnoreCase))
            {
                try {
                    Log("Starting EA Library Sync...");
                    var allEa = await _subService.GetAllEaGamesAsync();
                    var eaDataService = new EaDataService();
                    
                    // 1. Get Owned Offers (Long IDs)
                    var ownedOffers = await eaDataService.GetVaultOffersAsync();
                    Log($"Found {ownedOffers.Count} owned entitlements in Juno.");

                    // 2. Resolve to Numeric Content IDs (Launch IDs)
                    var resolvedOwned = await eaDataService.ResolveBatchOffersAsync(ownedOffers.Select(o => o.OfferId));
                    
                    // Handle duplicates safely (e.g. Dead Space 2008 vs 2023)
                    var contentIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in resolvedOwned)
                    {
                        var key = r.DisplayName.ToLowerInvariant().Replace("™", "").Replace("®", "").Trim();
                        if (!contentIdMap.ContainsKey(key))
                        {
                            contentIdMap[key] = r.ContentId;
                        }
                        else 
                        {
                            // If duplicate, maybe log it but keep existing? Or prioritize newer ID?
                            // Content IDs are usually incremental, so larger = newer?
                            if (long.TryParse(r.ContentId, out long newId) && long.TryParse(contentIdMap[key], out long oldId) && newId > oldId)
                            {
                                contentIdMap[key] = r.ContentId;
                            }
                        }
                    }
                    
                    // Debug Log
                    foreach(var kvp in contentIdMap.Take(5)) Log($"Map: {kvp.Key} -> {kvp.Value}");

                    var merged = new List<LocalLibraryService.InstalledGame>();
                    var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Helper for aggressive normalization
                    string NormalizeName(string name)
                    {
                        var n = name.ToLowerInvariant()
                            .Replace("™", "").Replace("®", "")
                            .Replace("digital deluxe", "")
                            .Replace("ultimate edition", "")
                            .Replace("goty edition", "")
                            .Replace("remastered", "")
                            .Replace("standard edition", "")
                            .Trim();
                        // Remove (YYYY)
                        n = System.Text.RegularExpressions.Regex.Replace(n, @"\(\d{4}\)", "");
                        // Remove special chars to ensure "Dead Space" == "Dead Space:"
                        n = new string(n.Where(c => char.IsLetterOrDigit(c)).ToArray());
                        return n;
                    }

                    // 3. Process Scraped Games (144 items)
                    foreach (var sg in allEa)
                    {
                        var normName = NormalizeName(sg.Name); // Use the strong normalizer for matching too
                        var game = new LocalLibraryService.InstalledGame { 
                            Name = sg.Name, 
                            Source = sg.IsPro ? "EA Play Pro" : "EA Play",
                            IsInstalled = false,
                            BackgroundImage = sg.Image
                        };

                        // Match with Resolved Content IDs
                        string? launchId = null;
                        // Try matching against contentIdMap keys (which were also normalized?)
                        // We need to re-normalize contentIdMap keys on the fly or just fuzzy match
                        var fuzzy = contentIdMap.FirstOrDefault(k => NormalizeName(k.Key) == normName).Value;
                        if (fuzzy != null) launchId = fuzzy;

                        if (!string.IsNullOrEmpty(launchId))
                        {
                            game.Id = launchId; // Set numeric ID
                            Log($"  [MATCH] {sg.Name} -> LaunchID: {game.Id}");
                        }
                        else
                        {
                            // Not in user's library - Use Name as ID for metadata/grids
                            game.Id = sg.Name;
                            game.Source += " (Not Redeemed)";
                        }

                        merged.Add(game);
                        processedNames.Add(normName); // Store NORMALIZED name
                    }

                    // 4. Add remaining Juno games that weren't in the scraper
                    foreach (var ro in resolvedOwned)
                    {
                        var cleanName = NormalizeName(ro.DisplayName);
                        
                        // Check if we already have a game that normalizes to this
                        if (!processedNames.Contains(cleanName))
                        {
                            merged.Add(new LocalLibraryService.InstalledGame {
                                Id = ro.ContentId,
                                Name = ro.DisplayName,
                                Source = "EA Play",
                                IsInstalled = false
                            });
                            processedNames.Add(cleanName); // Prevent internal Juno duplicates too
                        }
                    }

                    await _cacheService.SaveLibraryAsync("EA", merged);
                    Log($"EA Sync Complete. Merged {merged.Count} games.");
                } catch (Exception ex) { Log($"EA Sync Error: {ex.Message}"); }
            }

            if (source == null) _hasUpdated = true;
            Log("Full UpdateAllAsync Complete.");
        } 
        finally { _syncLock.Release(); }
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryUpdateService] {message}\n");
        } catch {{ }}
    }
}
