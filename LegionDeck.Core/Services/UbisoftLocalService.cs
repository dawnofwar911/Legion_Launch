using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LegionDeck.Core.Services;

[ProtoContract]
public class UplayCacheGame
{
    [ProtoMember(1)]
    public uint UplayId { get; set; }
    [ProtoMember(2)]
    public uint InstallId { get; set; }
    [ProtoMember(3)]
    public string? GameInfo { get; set; }
}

[ProtoContract]
public class UplayCacheGameCollection
{
    [ProtoMember(1)]
    public List<UplayCacheGame>? Games { get; set; }
}

public class UbisoftLocalService
{
    public static string ConfigurationsCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ubisoft Game Launcher",
        "cache",
        "configuration",
        "configurations");

    public uint? GetUplayIdByGameName(string gameName)
    {
        var cachePath = ConfigurationsCachePath;
        if (!File.Exists(cachePath)) return null;

        try
        {
            using var file = File.OpenRead(cachePath);
            var cacheData = Serializer.Deserialize<UplayCacheGameCollection>(file);
            if (cacheData?.Games == null) return null;

            string Normalize(string s) => new string(s.ToLower()
                .Replace("┬«", "")
                .Replace("┬á", " ")
                .Replace("Γäó", "")
                .Replace("ΓÇÖ", "'")
                .Replace("ΓÇô", "-")
                .Replace("┬", "")
                .Replace("«", "")
                .Replace("™", "")
                .Replace("®", "")
                .Where(char.IsLetterOrDigit).ToArray());

            var targetNorm = Normalize(gameName);
            var candidates = new List<(uint Id, string Name, int Score)>();

            foreach (var g in cacheData.Games)
            {
                if (string.IsNullOrEmpty(g.GameInfo)) continue;

                // 1. Extract Name (handling l1)
                var nameMatch = Regex.Match(g.GameInfo, @"name:\s*""?(.*?)""?\r?\n");
                string extractedName = nameMatch.Success ? nameMatch.Groups[1].Value : "";
                if (extractedName == "l1")
                {
                    var locMatch = Regex.Match(g.GameInfo, @"l1:\s*""?([^""]*?)""?\r?\n");
                    if (locMatch.Success) extractedName = locMatch.Groups[1].Value;
                }

                if (string.IsNullOrEmpty(extractedName) || extractedName == "is_visible: no") continue;

                string infoNorm = Normalize(g.GameInfo);
                string nameNorm = Normalize(extractedName);

                if (nameNorm == targetNorm || infoNorm.Contains(targetNorm))
                {
                    int score = 0;
                    // Playnite Rule: Actual games have 'start_game:' metadata
                    if (g.GameInfo.Contains("start_game:", StringComparison.OrdinalIgnoreCase)) score += 100;
                    
                    // Exact name match is high value
                    if (nameNorm == targetNorm) score += 50;

                    // DLCs/Packs are low value
                    if (nameNorm.Contains("seasonpass") || nameNorm.Contains("dlc") || nameNorm.Contains("pack")) score -= 80;

                    // Lower IDs are usually better (master IDs)
                    if (g.UplayId < 2000) score += 10;

                    candidates.Add((g.UplayId, extractedName, score));
                }
            }

            if (candidates.Count > 0)
            {
                var best = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Id).First();
                Log($"[Ubisoft Cache] Found {candidates.Count} candidates for '{gameName}'. BEST MATCH: {best.Id} ({best.Name}) with Score {best.Score}");
                return best.Id;
            }
        }
        catch (Exception ex)
        {
            Log($"[Ubisoft Cache] Error searching for {gameName}: {ex.Message}");
        }

        return null;
    }

    public List<uint> GetOwnedUplayIds()
    {
        var ids = new HashSet<uint>();
        var cachePath = ConfigurationsCachePath;
        
        if (!File.Exists(cachePath)) return ids.ToList();

        try
        {
            using var file = File.OpenRead(cachePath);
            var cacheData = Serializer.Deserialize<UplayCacheGameCollection>(file);
            if (cacheData?.Games != null)
            {
                foreach (var g in cacheData.Games)
                {
                    // Filter out DLCs/ULCs if needed, or keep everything owned
                    // Playnite keeps everything that isn't explicitly hidden
                    if (string.IsNullOrEmpty(g.GameInfo)) continue;
                    
                    // Optional: Skip if is_ulc: yes (technical items)
                    // But for ownership check, maybe we want to be broad?
                    // Let's stick to visible games + master IDs
                    
                    if (!g.GameInfo.Contains("is_visible: no", StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(g.UplayId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to parse Ubisoft configurations: {ex.Message}");
        }

        return ids.ToList();
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [UbisoftLocalService] {message}\n");
        }
        catch {{ }}
    }
}
