using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace EaDiagnostic;

class Program
{
    private const string GraphQlUrl = "https://service-aggregation-layer.juno.ea.com/graphql";
    private static readonly HttpClient _httpClient = new HttpClient();

    static async Task Main(string[] args)
    {
        Console.WriteLine("--- EA Juno Exhaustive Resolver ---");
        
        var authPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens", "ea_token.txt");
        if (!File.Exists(authPath)) { Console.WriteLine("Error: ea_token.txt not found."); return; }
        
        string token = File.ReadAllText(authPath);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var allResolved = new List<ResolvedGame>();
        string? cursor = "0";
        int page = 1;

        while (cursor != null)
        {
            Console.WriteLine($"\n[Page {page}] Fetching items (Cursor: {cursor})...");
            var (items, nextCursor) = await GetOwnedBatchAsync(cursor);
            
            if (items.Count > 0)
            {
                Console.WriteLine($"Resolving {items.Count} Content IDs...");
                var resolved = await ResolveBatchAsync(items.Select(i => i.OfferId).ToArray());
                foreach (var r in resolved)
                {
                    var original = items.FirstOrDefault(i => i.OfferId == r.OfferId);
                    allResolved.Add(new ResolvedGame {
                        Name = original?.Name ?? r.DisplayName,
                        OfferId = r.OfferId,
                        ContentId = r.ContentId
                    });
                }
            }

            cursor = nextCursor;
            if (cursor == "0" || string.IsNullOrEmpty(cursor)) cursor = null;
            page++;
            if (page > 30) break; // Safety
        }

        Console.WriteLine($"\nSUCCESS: Resolved {allResolved.Count} total games.");
        var sb = new StringBuilder();
        sb.AppendLine("Name,OfferId,ContentId");
        foreach (var g in allResolved.OrderBy(x => x.Name))
            sb.AppendLine($"\"{g.Name}\",\"{g.OfferId}\",\"{g.ContentId}\"");
        File.WriteAllText("juno_full_resolution.csv", sb.ToString());
        Console.WriteLine("Results saved to juno_full_resolution.csv");
    }

    static async Task<(List<ProductInfo> items, string? next)> GetOwnedBatchAsync(string next)
    {
        var list = new List<ProductInfo>();
        var requestBody = new {
            operationName = "GetVaultOffers",
            variables = new { 
                isMac = false, addFieldsToPreloadGames = true, locale = "en", limit = 40, next = next,
                type = new[] { "DIGITAL_FULL_GAME", "PACKAGED_FULL_GAME" },
                entitlementEnabled = true,
                storefronts = new[] { "EA", "STEAM", "EPIC" },
                ownershipMethods = new[] { "VAULT", "PURCHASE", "REDEMPTION" }, // Simplified
                platforms = new[] { "PC" }
            },
            extensions = new { persistedQuery = new { version = 1, sha256Hash = "5de4178ee7e1f084ce9deca856c74a9e03547a67dfafc0cb844d532fb54ae73d" } }
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(jsonBody));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var resp = await _httpClient.PostAsync(GraphQlUrl, content);
        var json = await resp.Content.ReadAsStringAsync();
        
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return (list, null);
        if (!data.TryGetProperty("me", out var me) || me.ValueKind != JsonValueKind.Object) return (list, null);
        if (!me.TryGetProperty("ownedGameProducts", out var ogp) || ogp.ValueKind != JsonValueKind.Object) return (list, null);
        
        string? nextCursor = ogp.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (!ogp.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return (list, nextCursor);

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string oid = item.TryGetProperty("originOfferId", out var oidProp) ? (oidProp.GetString() ?? "") : "";
            string name = "";
            if (item.TryGetProperty("product", out var prod) && prod.ValueKind == JsonValueKind.Object)
                name = prod.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
            
            if (!string.IsNullOrEmpty(oid)) list.Add(new ProductInfo { OfferId = oid, Name = name });
        }
        return (list, nextCursor);
    }

    static async Task<List<ResolvedGame>> ResolveBatchAsync(string[] offerIds)
    {
        var results = new List<ResolvedGame>();
        var requestBody = new {
            query = "query getLegacyCatalogDefs($offerIds: [String!]!, $locale: Locale) { legacyOffers(offerIds: $offerIds, locale: $locale) { offerId: id contentId displayName } }",
            operationName = "getLegacyCatalogDefs",
            variables = new { locale = "DEFAULT", offerIds = offerIds }
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(jsonBody));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var resp = await _httpClient.PostAsync(GraphQlUrl, content);
        var json = await resp.Content.ReadAsStringAsync();
        
        if (json.Contains("legacyOffers\":[]"))
        {
            Console.WriteLine($"\n[Debug] Batch failed for {offerIds.Length} items. First ID: {offerIds[0]}");
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("legacyOffers", out var offers))
        {
            foreach (var o in offers.EnumerateArray())
            {
                results.Add(new ResolvedGame {
                    OfferId = o.TryGetProperty("offerId", out var oid) ? (oid.GetString() ?? "") : "",
                    ContentId = o.TryGetProperty("contentId", out var cid) ? (cid.GetString() ?? "") : "",
                    DisplayName = o.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? "") : ""
                });
            }
        }
        return results;
    }

    class ProductInfo { public string Name { get; set; } = ""; public string OfferId { get; set; } = ""; }
    class ResolvedGame { public string Name { get; set; } = ""; public string OfferId { get; set; } = ""; public string ContentId { get; set; } = ""; public string DisplayName { get; set; } = ""; }
}
