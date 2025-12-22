using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace LegionDeck.Core.Services;

public class GameEnrichmentService
{
    private readonly MetadataService _metadataService;
    private readonly SteamStoreService _steamStoreService;
    private readonly IgdbService _igdbService;
    private readonly SteamGridDbService _sgdbService;

    public GameEnrichmentService(ConfigService configService, MetadataService metadataService)
    {
        _metadataService = metadataService;
        _steamStoreService = new SteamStoreService();
        _igdbService = new IgdbService(configService);
        _sgdbService = new SteamGridDbService(configService);
    }

    public async Task EnrichGameAsync(string gameId, string gameName, string source)
    {
        // 1. Enrich Name (especially for Steam Library where we only have ID)
        if (!_metadataService.HasName(gameId) || gameName.StartsWith("AppID "))
        {
            string? realName = null;
            if (source == "Steam" && int.TryParse(gameId, out int appId))
            {
                var details = await _steamStoreService.GetStoreDetailsAsync(appId);
                if (details != null && !string.IsNullOrEmpty(details.Name))
                {
                    realName = details.Name;
                }
            }

            if (!string.IsNullOrEmpty(realName))
            {
                _metadataService.SetName(gameId, realName);
                gameName = realName; // Update local variable for next steps
                Debug.WriteLine($"[Enrichment] Found name for {gameId}: {realName}");
            }
        }

        // 2. Enrich Hero Image
        if (!_metadataService.HasHero(gameId))
        {
            string? heroUrl = null;
            if (source == "Steam" && int.TryParse(gameId, out int appId))
            {
                heroUrl = await _sgdbService.GetHeroImageAsync(appId);
            }
            
            if (string.IsNullOrEmpty(heroUrl))
            {
                // Try searching by name if Steam ID failed or it's not a Steam game
                var sgdbId = await _sgdbService.SearchGameIdAsync(gameName);
                if (sgdbId.HasValue)
                {
                    heroUrl = await _sgdbService.GetHeroImageByGameIdAsync(sgdbId.Value);
                }
            }

            if (!string.IsNullOrEmpty(heroUrl))
            {
                _metadataService.SetHero(gameId, heroUrl);
                Debug.WriteLine($"[Enrichment] Found hero for {gameName}");
            }
        }

        // 2. Enrich Description
        if (!_metadataService.HasDescription(gameId))
        {
            string? description = null;
            
            // Try Steam first
            if (source == "Steam" && int.TryParse(gameId, out int appId))
            {
                var details = await _steamStoreService.GetStoreDetailsAsync(appId);
                if (details != null && !string.IsNullOrEmpty(details.ShortDescription))
                {
                    description = details.ShortDescription;
                }
            }
            else if (int.TryParse(gameId, out int steamAppId)) // Even if source != Steam, ID might be AppID (e.g. wishlist)
            {
                 var details = await _steamStoreService.GetStoreDetailsAsync(steamAppId);
                 if (details != null && !string.IsNullOrEmpty(details.ShortDescription))
                 {
                     description = details.ShortDescription;
                 }
            }

            // Fallback to IGDB
            if (string.IsNullOrEmpty(description))
            {
                description = await _igdbService.GetGameDescriptionAsync(gameName);
            }

            if (!string.IsNullOrEmpty(description))
            {
                _metadataService.SetDescription(gameId, description);
                 Debug.WriteLine($"[Enrichment] Found description for {gameName}");
            }
        }
    }
}
