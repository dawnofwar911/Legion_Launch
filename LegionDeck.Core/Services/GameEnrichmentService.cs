using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.IO;

namespace LegionDeck.Core.Services;

public class GameEnrichmentService
{
    private readonly MetadataService _metadataService;
    private readonly SteamStoreService _steamStoreService;
    private readonly IgdbService _igdbService;
    private readonly SteamGridDbService _sgdbService;
    private int _consecutiveErrors = 0;

    public GameEnrichmentService(ConfigService configService, MetadataService metadataService)
    {
        _metadataService = metadataService;
        _steamStoreService = new SteamStoreService();
        _igdbService = new IgdbService(configService);
        _sgdbService = new SteamGridDbService(configService);
    }

    public async Task<Dictionary<string, SteamStoreService.SteamStoreDetails>> EnrichGamesBatchAsync(
        List<(string Id, string Name, string Source)> games,
        Action<string, SteamStoreService.SteamStoreDetails>? onGameUpdated = null,
        CancellationToken cancellationToken = default)
    {
        var updatedDetails = new Dictionary<string, SteamStoreService.SteamStoreDetails>();
        
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [GameEnrichmentService] Starting enrichment for {games.Count} items.\n");
        } catch {}

        int concurrencyLevel = 5; 
        var batchTasks = new List<Task>();
        int processedCount = 0;
        _consecutiveErrors = 0;

        for (int i = 0; i < games.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var game = games[i];
            
            batchTasks.Add(Task.Run(async () =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                var idStr = game.Id;
                int.TryParse(idStr, out int appId); // 0 for non-Steam usually

                // 1. Try SGDB Cover (Vertical)
                try 
                {
                    var cached = _metadataService.GetCover(idStr);
                    if (string.IsNullOrEmpty(cached) || cached.Contains("steamstatic.com"))
                    {
                        string? sgdbCover = null;
                        
                        // A. Try by AppID if Steam
                        if (game.Source == "Steam" && appId > 0)
                        {
                            sgdbCover = await _sgdbService.GetVerticalCoverAsync(appId);
                        }
                        
                        // B. Try by Name (Fallback or non-Steam like EA)
                        if (string.IsNullOrEmpty(sgdbCover))
                        {
                            var nameToSearch = _metadataService.GetName(idStr) ?? game.Name;
                            
                            // Clean name for better search results (remove trademarks etc)
                            nameToSearch = nameToSearch.Replace("™", "").Replace("®", "").Trim();

                            if (!string.IsNullOrEmpty(nameToSearch))
                            {
                                var sgdbId = await _sgdbService.SearchGameIdAsync(nameToSearch);
                                if (sgdbId.HasValue) 
                                {
                                    sgdbCover = await _sgdbService.GetVerticalCoverByGameIdAsync(sgdbId.Value);
                                    
                                    // Also fetch Hero if we have the ID handy
                                    if (!_metadataService.HasHero(idStr))
                                    {
                                        var hero = await _sgdbService.GetHeroImageByGameIdAsync(sgdbId.Value);
                                        if (!string.IsNullOrEmpty(hero)) _metadataService.SetHero(idStr, hero, false);
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(sgdbCover))
                        {
                            _metadataService.SetCover(idStr, sgdbCover, false);
                            var coverDetails = new SteamStoreService.SteamStoreDetails 
                            { 
                                VerticalCover = sgdbCover, 
                                Name = _metadataService.GetName(idStr) ?? game.Name 
                            };
                            onGameUpdated?.Invoke(idStr, coverDetails);
                        }
                    }
                } catch {}

                // 2. Try Steam Store (Type/Desc) - ONLY FOR STEAM
                if (game.Source == "Steam" && appId > 0)
                {
                    SteamStoreService.SteamStoreDetails? details = null;
                    try
                    {
                        details = await _steamStoreService.GetStoreDetailsAsync(appId);
                    }
                    catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests || ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        await Task.Delay(60000); 
                        try { details = await _steamStoreService.GetStoreDetailsAsync(appId); } catch { }
                    }
                    catch { }

                    if (details != null)
                    {
                        Interlocked.Exchange(ref _consecutiveErrors, 0);
                        bool updated = false;
                        if (!string.IsNullOrEmpty(details.Name)) { _metadataService.SetName(idStr, details.Name, false); updated = true; }
                        if (!string.IsNullOrEmpty(details.ShortDescription)) _metadataService.SetDescription(idStr, details.ShortDescription, false);
                        if (!string.IsNullOrEmpty(details.Type)) { _metadataService.SetType(idStr, details.Type, false); updated = true; }
                        
                        if (updated) onGameUpdated?.Invoke(idStr, details);
                        lock(updatedDetails) { updatedDetails[idStr] = details; }
                    }
                    else
                    {
                        Interlocked.Increment(ref _consecutiveErrors);
                    }
                }
                
                Interlocked.Increment(ref processedCount);
            }));

            if (batchTasks.Count >= concurrencyLevel)
            {
                await Task.WhenAny(batchTasks);
                batchTasks.RemoveAll(t => t.IsCompleted);
                await Task.Delay(1000); // Politeness delay
            }
        }
        
        await Task.WhenAll(batchTasks);
        
        // Save all caches once at the end
        _metadataService.SaveCoverCache();
        _metadataService.SaveDescriptionCache();
        _metadataService.SaveNameCache();
        _metadataService.SaveTypeCache();
        _metadataService.SaveHeroCache();
        
        return updatedDetails;
    }

    public async Task EnrichGameAsync(string gameId, string gameName, string source)
    {
        if (!_metadataService.HasName(gameId) || gameName.StartsWith("AppID "))
        {
            if (source == "Steam" && int.TryParse(gameId, out int appId))
            {
                var details = await _steamStoreService.GetStoreDetailsAsync(appId);
                if (details != null && !string.IsNullOrEmpty(details.Name)) _metadataService.SetName(gameId, details.Name);
            }
        }

        if (!_metadataService.HasHero(gameId))
        {
            string? heroUrl = null;
            if (source == "Steam" && int.TryParse(gameId, out int appId)) heroUrl = await _sgdbService.GetHeroImageAsync(appId);
            if (string.IsNullOrEmpty(heroUrl))
            {
                var sgdbId = await _sgdbService.SearchGameIdAsync(gameName);
                if (sgdbId.HasValue) heroUrl = await _sgdbService.GetHeroImageByGameIdAsync(sgdbId.Value);
            }
            if (!string.IsNullOrEmpty(heroUrl)) _metadataService.SetHero(gameId, heroUrl);
        }

        if (!_metadataService.HasDescription(gameId))
        {
            string? description = null;
            if (source == "Steam" && int.TryParse(gameId, out int appId))
            {
                var details = await _steamStoreService.GetStoreDetailsAsync(appId);
                if (details != null) description = details.ShortDescription;
            }
            if (string.IsNullOrEmpty(description)) description = await _igdbService.GetGameDescriptionAsync(gameName);
            if (!string.IsNullOrEmpty(description)) _metadataService.SetDescription(gameId, description);
        }
    }
}
