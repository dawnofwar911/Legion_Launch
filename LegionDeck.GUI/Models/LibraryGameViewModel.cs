using LegionDeck.Core.Services;

namespace LegionDeck.GUI.Models;

public class LibraryGameViewModel
{
    public LocalLibraryService.InstalledGame GameData { get; }
    public string Name => GameData.Name;
    public string Source => GameData.Source;
    public string ImgCapsule { get; set; }

    public LibraryGameViewModel(LocalLibraryService.InstalledGame game)
    {
        GameData = game;
        if (game.Source == "Steam")
            ImgCapsule = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.Id}/library_600x900_2x.jpg";
        else
            ImgCapsule = "";
    }
}
