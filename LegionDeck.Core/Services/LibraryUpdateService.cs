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
                try
                {
                    Log("Starting EA Library Sync...");
                    var allEa = await _subService.GetAllEaGamesAsync();
                    var eaDataService = new EaDataService();
                    var standardNames = new HashSet<string>(allEa.Where(g => g.IsStandard).Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
                    
                    var merged = allEa.Where(g => g.IsStandard).Select(i => new LocalLibraryService.InstalledGame { Id = i.Id, Name = i.Name, Source = "EA Play", IsInstalled = false, BackgroundImage = i.Image }).ToList();
                    foreach(var pg in allEa.Where(g => g.IsPro))
                    {
                        if (!standardNames.Contains(pg.Name))
                            merged.Add(new LocalLibraryService.InstalledGame { Id = pg.Id, Name = pg.Name, Source = "EA Play Pro", IsInstalled = false, BackgroundImage = pg.Image });
                    }

                    Log($"Scraped {merged.Count} EA games. Resolving Vault metadata...");
                    var vaultOffers = await eaDataService.GetVaultOffersAsync();
                    
                    if (vaultOffers.Any())
                    {
                        Log($"Resolving {vaultOffers.Count} vault items to get launchable Content IDs...");
                        // We must resolve these long IDs (e.g. en-us_a-way-out...) to numeric Content IDs
                        var resolvedVault = await eaDataService.ResolveBatchOffersAsync(vaultOffers.Select(v => v.OfferId));
                        
                        int matchedCount = 0;
                        foreach (var resolved in resolvedVault)
                        {
                            if (string.IsNullOrEmpty(resolved.ContentId)) continue;

                            // Find the best match in our scraped list
                            var normResolved = resolved.DisplayName.ToLowerInvariant().Replace("™", "").Replace("®", "").Replace("ea play pro edition", "").Trim();
                            
                            var match = merged.FirstOrDefault(g => {
                                var normScraped = g.Name.ToLowerInvariant().Replace("™", "").Replace("®", "").Trim();
                                return normScraped.Contains(normResolved) || normResolved.Contains(normScraped);
                            });

                            if (match != null)
                            {
                                Log($"Matched: '{match.Name}' -> ID: {resolved.ContentId}");
                                match.Id = resolved.ContentId;
                                matchedCount++;
                            }
                        }
                        Log($"Vault Sync: Successfully identified {matchedCount} launchable games.");
                    }

                    _localService.UpdateInstallationStatus(merged, localGames);
                    await _cacheService.SaveLibraryAsync("EA", merged);
                    Log("EA Sync Complete.");
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
