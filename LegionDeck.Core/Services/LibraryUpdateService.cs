using System;
using System.Collections.Generic;
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
            var allEa = await _subService.GetAllEaGamesAsync();
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
            _localService.UpdateInstallationStatus(merged, localGames);
            await _cacheService.SaveLibraryAsync("EA", merged);
        } catch { }
    }
}
