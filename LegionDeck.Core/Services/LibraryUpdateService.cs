using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public class LibraryUpdateService
{
    private static bool _hasUpdated = false;
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

    public async Task UpdateAllAsync()
    {
        if (_hasUpdated) return;
        _hasUpdated = true;

        var localGames = await _localService.GetInstalledGamesAsync();

        // 1. Steam
        try
        {
            var items = await _steamService.GetOwnedGamesAsync();
            var list = items.Select(i => new LocalLibraryService.InstalledGame { Id = i.AppId.ToString(), Name = i.Name, Source = "Steam", IsInstalled = false }).ToList();
            _localService.UpdateInstallationStatus(list, localGames);
            await _cacheService.SaveLibraryAsync("Steam", list);
        } catch { }

        // 2. Xbox
        try
        {
            var xboxService = new XboxLibraryService();
            var catalog = await xboxService.GetGamePassGamesAsync();
            var list = catalog.Select(item => new LocalLibraryService.InstalledGame 
            { 
                Id = item.SteamAppId ?? item.Name, 
                Name = item.Name, 
                Source = "Xbox", 
                IsInstalled = false 
            }).ToList();
            _localService.UpdateInstallationStatus(list, localGames);
            await _cacheService.SaveLibraryAsync("Xbox", list);
        } catch { }

        // 3. Ubisoft
        try
        {
            var items = await _subService.GetUbisoftPlusGamesAsync();
            var list = items.Select(i => new LocalLibraryService.InstalledGame { Id = i.Name, Name = i.Name, Source = "Ubisoft", IsInstalled = false }).ToList();
            _localService.UpdateInstallationStatus(list, localGames);
            await _cacheService.SaveLibraryAsync("Ubisoft", list);
        } catch { }

        // 4. EA
        try
        {
            Log("Starting EA Library Sync...");
            var allEa = await _subService.GetAllEaGamesAsync();
            var eaDataService = new EaDataService();
            var standardNames = new HashSet<string>(allEa.Where(g => g.IsStandard).Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            
            var merged = allEa.Where(g => g.IsStandard).Select(i => new LocalLibraryService.InstalledGame 
            { 
                Id = i.Id, Name = i.Name, Source = "EA Play", IsInstalled = false, BackgroundImage = i.Image 
            }).ToList();

            foreach(var pg in allEa.Where(g => g.IsPro))
            {
                if (!standardNames.Contains(pg.Name))
                {
                    merged.Add(new LocalLibraryService.InstalledGame 
                    { 
                        Id = pg.Id, Name = pg.Name, Source = "EA Play Pro", IsInstalled = false, BackgroundImage = pg.Image
                    });
                }
            }

            Log($"Scraped {merged.Count} EA games. Resolving Vault IDs...");

            // Step 1: Get Vault Games (Origin.OFR IDs)
            var vaultOffers = await eaDataService.GetVaultOffersAsync();
            
            // Step 2: Batch Resolve ALL Vault Offers to get their real names and Content IDs
            // (Juno often returns null product info in the vault list, so we must resolve them)
            var offerIdsToResolve = vaultOffers.Select(v => v.OfferId).ToList();
            var resolvedVaultGames = new List<EaDataService.EaOffer>();

            if (offerIdsToResolve.Count > 0)
            {
                Log($"Resolving {offerIdsToResolve.Count} vault offers to get metadata...");
                resolvedVaultGames = await eaDataService.ResolveBatchOffersAsync(offerIdsToResolve);
            }

            // Step 3: Match Scraped Games to Resolved Vault Games
            int updatedCount = 0;
            var matchedGames = new HashSet<string>();

            foreach (var resolved in resolvedVaultGames)
            {
                if (string.IsNullOrEmpty(resolved.ContentId)) continue;

                // Create a normalized slug for the resolved vault game
                var vaultSlug = resolved.DisplayName.ToLowerInvariant()
                    .Replace(" ", "-").Replace(":", "").Replace("'", "").Replace("™", "").Replace("®", "");

                // Try to find this game in our scraped list
                var match = merged.FirstOrDefault(g => 
                {
                    var gameSlug = g.Name.ToLowerInvariant()
                        .Replace(" ", "-").Replace(":", "").Replace("'", "").Replace("™", "").Replace("®", "");
                    
                    // 1. Exact Name Match
                    if (g.Name.Equals(resolved.DisplayName, StringComparison.OrdinalIgnoreCase)) return true;
                    
                    // 2. Slug Match (contains)
                    if (gameSlug.Contains(vaultSlug) || vaultSlug.Contains(gameSlug)) return true;

                    // 3. Special handling for "Pro Edition" suffixes
                    if (resolved.DisplayName.Contains(g.Name) && resolved.DisplayName.Contains("Pro Edition")) return true;

                    return false;
                });

                if (match != null)
                {
                    // Only update if we found a new/better ID
                    if (match.Id != resolved.ContentId)
                    {
                        Log($"Matched & Updated: '{match.Name}' ({match.Id}) -> '{resolved.DisplayName}' ({resolved.ContentId})");
                        match.Id = resolved.ContentId;
                        matchedGames.Add(match.Id);
                        updatedCount++;
                    }
                }
            }
            
            Log($"Vault Sync: Updated {updatedCount} games with verified Content IDs.");

            // Step 4: Fallback for unmatched games (Resolve by Slug)
            // Only try this for a limited number of games to avoid API spam, or prioritize "EA Play Pro" titles
            var unmatched = merged.Where(g => !matchedGames.Contains(g.Id) && g.Source == "EA Play Pro").Take(5).ToList();
            if (unmatched.Count > 0)
            {
                Log($"Attempting slug resolution for {unmatched.Count} unmatched Pro titles...");
                foreach (var game in unmatched)
                {
                    var slug = game.Name.ToLowerInvariant().Replace(" ", "-").Replace(":", "").Replace("'", "");
                    var offer = await eaDataService.ResolveOfferAsync(slug);
                    if (offer != null && !string.IsNullOrEmpty(offer.ContentId))
                    {
                        Log($"Slug Resolved: '{game.Name}' -> {offer.ContentId}");
                        game.Id = offer.ContentId;
                    }
                }
            }

            _localService.UpdateInstallationStatus(merged, localGames);
            await _cacheService.SaveLibraryAsync("EA", merged);
            Log("EA Library Sync Complete.");
        } catch (Exception ex) { Log($"EA Library Sync Error: {ex.Message}"); }
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LibraryUpdateService] {message}\n");
        }
        catch { }
    }
}
