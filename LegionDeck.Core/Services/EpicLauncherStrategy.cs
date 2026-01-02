using System.Diagnostics;
using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class EpicLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.ToLower() == "epic";

    public Task LaunchAsync(InstalledGame game)
    {
        string uri = $"com.epicgames.launcher://apps/{game.Id}?action=launch&silent=true";
        LocalLibraryService.Log($"[Epic] Launching {game.Name} via URI: {uri}");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
