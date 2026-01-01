using System;
using System.Collections.Generic;

namespace LegionDeck.Core.Services;

public class UbisoftMasterIdService
{
    private static readonly Dictionary<string, uint> MasterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "For Honor", 569 },
        { "Assassin's Creed Valhalla", 13504 },
        { "Assassin's Creed Valhalla Complete Edition", 13504 },
        { "Assassin's Creed Odyssey", 5059 },
        { "Assassin's Creed Origins", 3539 },
        { "Assassin's Creed Mirage", 6100 },
        { "Avatar: Frontiers of Pandora", 4740 },
        { "Far Cry 6", 5266 },
        { "Far Cry 5", 1803 },
        { "WATCH_DOGS 2", 2688 },
        { "Watch Dogs: Legion", 3353 },
        { "Tom Clancy's Rainbow Six Siege", 635 },
        { "Tom Clancy's The Division 2", 4932 },
        { "Tom Clancy's Ghost Recon Breakpoint", 11903 },
        { "Riders Republic", 5487 },
        { "Anno 1800", 4553 },
        { "Anno 117 - Pax Romana", 921 },
        { "Anno 117: Pax Romana Gold Edition", 921 },
        { "Immortals Fenyx Rising", 5405 },
        { "The Crew Motorfest", 16732 },
        { "The Settlers: New Allies", 3037 },
        { "Prince of Persia: The Lost Crown", 6145 },
        { "Star Wars: Outlaws", 17903 },
        { "Skull and Bones", 1713 }
    };

    public uint? GetMasterId(string name)
    {
        // Direct match
        if (MasterIds.TryGetValue(name, out var id)) return id;

        // Strip "Complete Edition", "Ultimate Edition", etc.
        string cleanName = name.Replace("Complete Edition", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("Ultimate Edition", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("Gold Edition", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("Standard Edition", "", StringComparison.OrdinalIgnoreCase)
                               .Replace(" Deluxe", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("™", "")
                               .Replace("®", "")
                               .TrimEnd(' ', '-', ':', '–');

        if (MasterIds.TryGetValue(cleanName, out var id2)) return id2;

        return null;
    }
}
