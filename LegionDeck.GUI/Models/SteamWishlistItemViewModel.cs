using Microsoft.UI.Xaml;
using LegionDeck.Core.Models;

namespace LegionDeck.GUI.Models;

public class SteamWishlistItemViewModel : SteamWishlistItem
{
    public SteamWishlistItemViewModel() { }

    public SteamWishlistItemViewModel(SteamWishlistItem item)
    {
        this.AppId = item.AppId;
        this.Name = item.Name;
        // Default to Steam vertical art
        this.ImgCapsule = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{item.AppId}/library_600x900_2x.jpg";
    }

    public bool IsOnGamePass { get; set; }
    public bool IsOnEaPlay { get; set; }
    public bool IsOnUbisoftPlus { get; set; }
    public bool UserHasAccess { get; set; }

    public Visibility GamePassVisibility => IsOnGamePass ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EaPlayVisibility => IsOnEaPlay ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UbisoftPlusVisibility => IsOnUbisoftPlus ? Visibility.Visible : Visibility.Collapsed;
    
    public string AccessStatus => UserHasAccess ? "FREE TO PLAY" : "";
    public Visibility AccessVisibility => UserHasAccess ? Visibility.Visible : Visibility.Collapsed;
}
