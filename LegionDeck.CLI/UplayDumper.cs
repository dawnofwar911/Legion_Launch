using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace LegionDeck.CLI;

[ProtoContract]
public class UplayCacheGame {
    [ProtoMember(1)] public uint UplayId { get; set; }
    [ProtoMember(2)] public uint InstallId { get; set; }
    [ProtoMember(3)] public string? GameInfo { get; set; }
}

[ProtoContract]
public class UplayCacheGameCollection {
    [ProtoMember(1)] public List<UplayCacheGame>? Games { get; set; }
}

public class UplayDumper {
    public static void Dump() {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubisoft Game Launcher", "cache", "configuration", "configurations");
        if (!File.Exists(path)) {
            Console.WriteLine("Cache file not found at: " + path);
            return;
        }

        try {
            using var file = File.OpenRead(path);
            var cacheData = Serializer.Deserialize<UplayCacheGameCollection>(file);
            
            var targetIds = new uint[] { 569, 61435, 13504, 5607 };
            foreach (var g in cacheData?.Games ?? new()) {
                if (targetIds.Contains(g.UplayId))
                {
                    Console.WriteLine($"--- FULL INFO FOR {g.UplayId} ---");
                    Console.WriteLine(g.GameInfo);
                    Console.WriteLine("-------------------------");
                }
                
                if (string.IsNullOrEmpty(g.GameInfo)) continue;

                // Attempt to find a real name
                var nameMatch = Regex.Match(g.GameInfo, @"name:\s*""?(.*?)""?\r?\n");
                string name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown";

                if (name == "l1")
                {
                    var locMatch = Regex.Match(g.GameInfo, @"l1:\s*""?(.*?)""?\r?\n");
                    if (locMatch.Success) name = locMatch.Groups[1].Value;
                }

                if (g.GameInfo.Contains("is_visible: no", StringComparison.OrdinalIgnoreCase)) continue;

                Console.WriteLine($"ID: {g.UplayId} | Name: {name}");
            }
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}