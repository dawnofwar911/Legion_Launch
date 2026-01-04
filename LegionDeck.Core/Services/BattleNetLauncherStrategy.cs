using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using static LegionDeck.Core.Services.LocalLibraryService;

namespace LegionDeck.Core.Services;

public class BattleNetLauncherStrategy : ILauncherStrategy
{
    public bool CanHandle(string source) => source?.ToLower() == "battle.net";

    public Task LaunchAsync(InstalledGame game)
    {
        string? bnetPath = GetBattleNetPath();
        if (!string.IsNullOrEmpty(bnetPath) && File.Exists(bnetPath))
        {
            LocalLibraryService.Log($"[Battle.net] Launching {game.Name} via Executable: {bnetPath} --exec=\"launch {game.Id}\"");
            Process.Start(new ProcessStartInfo
            {
                FileName = bnetPath,
                Arguments = $"--exec=\"launch {game.Id}\"",
                UseShellExecute = false
            });
        }
        else
        {
            string uri = game.LaunchUri ?? $"battlenet://{game.Id}";
            LocalLibraryService.Log($"[Battle.net] Launching {game.Name} via URI (Fallback): {uri}");
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        return Task.CompletedTask;
    }

    private string? GetBattleNetPath()
    {
        try
        {
            // Check common install locations via registry
            string? path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net\Capabilities", "ApplicationIcon", null) as string;
            if (!string.IsNullOrEmpty(path))
            {
                return path.Split(',')[0].Trim('"'); // Remove icon index if present
            }

            path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Blizzard Entertainment\Battle.net\Capabilities", "ApplicationIcon", null) as string;
            if (!string.IsNullOrEmpty(path))
            {
                return path.Split(',')[0].Trim('"');
            }
        }
        catch { }
        return null;
    }
}
