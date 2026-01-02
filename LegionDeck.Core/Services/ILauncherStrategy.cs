using System.Threading.Tasks;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public interface ILauncherStrategy
{
    bool CanHandle(string source);
    Task LaunchAsync(InstalledGame game);
}
