using System;
using System.Diagnostics;
using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class EaLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.StartsWith("EA", StringComparison.OrdinalIgnoreCase) == true;

    public Task LaunchAsync(InstalledGame game)
    {
        string? uri;
        if (!game.IsInstalled)
        {
            if (string.IsNullOrEmpty(game.Id) || game.Id.StartsWith("Origin.OFR"))
            {
                LocalLibraryService.Log($"[EA] No numeric Content ID found for {game.Name} (ID: {game.Id}). Opening Library.");
                uri = "origin2://library/open";
            }
            else
            {
                uri = BuildEaUri(game);
            }
        }
        else
        {
            uri = game.LaunchUri;
            if (string.IsNullOrEmpty(uri))
            {
                uri = BuildEaUri(game);
            }
        }

        if (!string.IsNullOrEmpty(uri))
        {
            LocalLibraryService.Log($"[EA] Launching {game.Name} via URI: {uri}");
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        
        return Task.CompletedTask;
    }

    private string? BuildEaUri(InstalledGame game)
    {
        if (string.IsNullOrEmpty(game.Id)) return "origin2://library/open";

        // Generate slug from name
        string slug = game.Name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(":", "")
            .Replace("'", "")
            .Replace("™", "")
            .Replace("®", "")
            .Replace(".", "")
            .Replace("!", "");

        if (game.IsInstalled || (!string.IsNullOrEmpty(game.Id) && !game.Id.StartsWith("Origin.OFR")))
        {
             // Numeric ID (Content ID) -> Installer or Launch
             return $"origin2://game/launch/?offerIds={game.Id}&slug={slug}&autoDownload=true";
        }
        else 
        {
            // No valid Content ID -> Store/Library Page fallback
            return $"origin2://store/open?slug={slug}";
        }
    }
}
