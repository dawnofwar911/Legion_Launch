using System;
using System.Diagnostics;
using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class UbisoftLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.StartsWith("Ubisoft", StringComparison.OrdinalIgnoreCase) == true;

    public Task LaunchAsync(InstalledGame game)
    {
        string? uri = null;

        if (!game.IsInstalled)
        {
            if (game.LaunchUri != null && game.LaunchUri.StartsWith("INSTALL:"))
            {
                string lid = game.LaunchUri.Substring(8);
                LocalLibraryService.Log($"[Ubisoft] --- ATTEMPTING CACHE INSTALL --- ID: {lid}");
                uri = $"uplay://install/{lid}";
            }
            else
            {
                LocalLibraryService.Log($"[Ubisoft] Game not in local cache or marked as unclaimed. Launching Ubisoft Connect app.");
                uri = "uplay://";
            }
        }
        else
        {
            uri = $"uplay://launch/{game.Id}/0";
        }

        // Final Catch-all for Ubisoft to ensure app opens
        if (string.IsNullOrEmpty(uri))
        {
            LocalLibraryService.Log($"[Ubisoft] No URI determined for {game.Name}. Falling back to uplay://");
            uri = "uplay://";
        }

        LocalLibraryService.Log($"[Ubisoft] Launching via URI: {uri}");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
