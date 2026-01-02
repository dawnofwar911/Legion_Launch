using System.Diagnostics;
using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class SteamLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.ToLower() == "steam";

    public Task LaunchAsync(InstalledGame game)
    {
        string uri = game.IsInstalled 
            ? $"steam://run/{game.Id}" 
            : $"steam://install/{game.Id}";
            
        LocalLibraryService.Log($"[Steam] {(game.IsInstalled ? "Launching" : "Installing")} {game.Name} via URI: {uri}");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
