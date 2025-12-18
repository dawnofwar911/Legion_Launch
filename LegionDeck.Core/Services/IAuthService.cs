using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

public interface IAuthService
{
    Task<string?> LoginAsync();
    void ClearCookies();
}