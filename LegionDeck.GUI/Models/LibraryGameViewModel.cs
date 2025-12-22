using LegionDeck.Core.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LegionDeck.GUI.Models;

public class LibraryGameViewModel : INotifyPropertyChanged
{
    public LocalLibraryService.InstalledGame GameData { get; }
    
    private string _name;
    public string Name 
    { 
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Source => GameData.Source;
    
    private string _imgCapsule;
    public string ImgCapsule 
    { 
        get => _imgCapsule;
        set
        {
            if (_imgCapsule != value)
            {
                _imgCapsule = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryGameViewModel(LocalLibraryService.InstalledGame game)
    {
        GameData = game;
        _name = game.Name;
        if (game.Source == "Steam")
            _imgCapsule = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.Id}/library_600x900_2x.jpg";
        else
            _imgCapsule = "https://cdn.cloudflare.steamstatic.com/steam/apps/480/library_600x900_2x.jpg"; // Fallback to Spacewar or similar
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
