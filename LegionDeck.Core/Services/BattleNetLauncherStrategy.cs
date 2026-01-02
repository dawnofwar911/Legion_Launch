using System.Diagnostics;
using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class BattleNetLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.ToLower() == "battle.net";

    public Task LaunchAsync(InstalledGame game)
    {
        string uri = game.LaunchUri ?? $"battlenet://{game.Id}";
        LocalLibraryService.Log($"[Battle.net] Launching {game.Name} via URI: {uri}");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
