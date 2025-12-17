using System.Threading.Tasks;

namespace LegionDeck.CLI.Services;

public interface IAuthService
{
    Task<string?> LoginAsync();
}
