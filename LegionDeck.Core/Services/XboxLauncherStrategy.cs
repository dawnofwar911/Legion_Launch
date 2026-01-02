using System.Diagnostics;
using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class XboxLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.ToLower() == "xbox";

    public Task LaunchAsync(InstalledGame game)
    {
        string uri;
        if (game.IsInstalled)
        {
            uri = game.LaunchUri ?? $"ms-windows-store://pdp/?ProductId={game.Id}";
        }
        else
        {
            uri = $"msxbox://game/?productId={game.Id}";
        }

        LocalLibraryService.Log($"[Xbox] {(game.IsInstalled ? "Launching" : "Installing")} {game.Name} via URI: {uri}");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
