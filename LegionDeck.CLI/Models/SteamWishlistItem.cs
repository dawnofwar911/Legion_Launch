namespace LegionDeck.CLI.Models;

public class SteamWishlistItem
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> PlainIds { get; set; } = new List<string>();
    public string ImgCapsule { get; set; } = string.Empty;
    public string SmallCapsule { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // e.g., "Game", "DLC"
    public int ReviewScore { get; set; }
    public string ReviewDesc { get; set; } = string.Empty;
    public int ReviewsTotal { get; set; }
    public int ReviewsPercent { get; set; }
    public string ReleaseDate { get; set; } = string.Empty;
    public bool ReleaseDateComingSoon { get; set; }
    public int Priority { get; set; }
    public decimal? Price { get; set; } // Current price (may not always be present)
    public decimal? OriginalPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int Added { get; set; } // Timestamp
    public bool IsFree { get; set; }
    public bool OnSale { get; set; }

    // This model might need adjustment based on the exact JSON structure returned by Steam's wishlistdata.
}
