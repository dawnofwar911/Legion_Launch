using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace LegionDeck.Core.Services;

public class EaDataService
{
    private const string GraphQlUrl = "https://service-aggregation-layer.juno.ea.com/graphql";
    private readonly HttpClient _httpClient;
    private readonly string _eaCookieFilePath;

    public EaDataService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        _eaCookieFilePath = Path.Combine(authTokensPath, "ea_cookies.json");
    }

    public class EaOffer
    {
        public string OfferId { get; set; } = "";
        public string ContentId { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public async Task<List<EaOffer>> GetVaultOffersAsync()
    {
        var results = new List<EaOffer>();
        var token = await GetAuthTokenAsync();
        if (string.IsNullOrEmpty(token)) return results;

        string? nextCursor = "0"; 
        int limit = 40; 
        int safetyCounter = 0;

        Log("[Juno] Starting Exhaustive Paginated Scan...");

        while (!string.IsNullOrEmpty(nextCursor) && safetyCounter < 25)
        {
            safetyCounter++;
            var variables = new {
                isMac = false, addFieldsToPreloadGames = true, locale = "en", limit = limit, next = nextCursor,
                type = new[] { "DIGITAL_FULL_GAME", "PACKAGED_FULL_GAME" }, entitlementEnabled = true,
                storefronts = new[] { "EA", "STEAM", "EPIC" },
                ownershipMethods = new[] { 
                    "UNKNOWN", "ASSOCIATION", "PURCHASE", "REDEMPTION", "GIFT_RECEIPT", "ENTITLEMENT_GRANT", 
                    "DIRECT_ENTITLEMENT", "PRE_ORDER_PURCHASE", "VAULT", "XGP_VAULT", "STEAM", "STEAM_VAULT", 
                    "STEAM_SUBSCRIPTION", "EPIC", "EPIC_VAULT", "EPIC_SUBSCRIPTION", "XGP_SUBSCRIPTION"
                },
                platforms = new[] { "PC" }
            };
            var extensions = new { persistedQuery = new { version = 1, sha256Hash = "5de4178ee7e1f084ce9deca856c74a9e03547a67dfafc0cb844d532fb54ae73d" } };
            var uri = $"{GraphQlUrl}?operationName=getPreloadedOwnedGames&variables={Uri.EscapeDataString(JsonSerializer.Serialize(variables))}&extensions={Uri.EscapeDataString(JsonSerializer.Serialize(extensions))}";

            try {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (json.Contains("UNAUTHENTICATED")) {
                    Log("[Juno] Token expired during scan. Refreshing...");
                    var authService = new EaAuthService();
                    if (await authService.RefreshTokenAsync()) { token = await GetAuthTokenAsync(); continue; }
                    break;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.TryGetProperty("me", out var me) && 
                    me.TryGetProperty("ownedGameProducts", out var owned))
                {
                    var items = owned.GetProperty("items");
                    int count = 0;
                    foreach (var item in items.EnumerateArray()) {
                        if (item.ValueKind == JsonValueKind.Null) continue;
                        string offerId = item.GetProperty("originOfferId").GetString() ?? "";
                        string displayName = item.TryGetProperty("product", out var p) && p.ValueKind != JsonValueKind.Null ? (p.GetProperty("name").GetString() ?? "") : "";
                        if (!string.IsNullOrEmpty(offerId) && !results.Any(r => r.OfferId == offerId)) {
                            results.Add(new EaOffer { OfferId = offerId, DisplayName = displayName });
                            count++;
                        }
                    }
                    
                    var receivedNext = owned.TryGetProperty("next", out var nxt) ? nxt.GetString() : null;
                    Log($"[Juno] Page {safetyCounter}: Found {count} items. Next Cursor: {receivedNext ?? "NULL"}. Total unique: {results.Count}");
                    
                    if (string.IsNullOrEmpty(receivedNext) || receivedNext == "0" || receivedNext == nextCursor)
                        nextCursor = null;
                    else 
                        nextCursor = receivedNext;
                }
                else nextCursor = null;
            } catch { nextCursor = null; }
        }

        Log("[Juno] Exhaustive Scan Complete. Final count: {results.Count}");
        return results;
    }

    public async Task<EaOffer?> ResolveOfferAsync(string slug)
    {
        var result = await ResolveOfferInternalAsync(slug);
        if (result != null) return result;

        var simpleSlug = new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()).Replace("--", "-");
        if (simpleSlug != slug)
        {
            result = await ResolveOfferInternalAsync(simpleSlug);
            if (result != null) return result;
        }
        return null;
    }

    private async Task<EaOffer?> ResolveOfferInternalAsync(string slug)
    {
        var token = await GetAuthTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        var variables = new { locale = "en", subscriptionLevel = "PREMIUM", gameId = slug, overrideCountryCode = "GB" };
        var extensions = new { persistedQuery = new { version = 1, sha256Hash = "1b08dff7328b969bfefc4ee05b3eeeb6980552ede8b857b0c46c471edd12d14b" } };
        var uri = $"{GraphQlUrl}?operationName=GameOffers&variables={Uri.EscapeDataString(JsonSerializer.Serialize(variables))}&extensions={Uri.EscapeDataString(JsonSerializer.Serialize(extensions))}";

        try {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null) {
                // First try to get the most specific ID (the one Juno itself returns for the game)
                string fallbackId = game.TryGetProperty("id", out var gid) ? (gid.GetString() ?? "") : "";

                if (game.TryGetProperty("eaPlayProOffer", out var pro) && pro.ValueKind != JsonValueKind.Null) return ParseOffer(pro);
                if (game.TryGetProperty("eaPlayOffer", out var std) && std.ValueKind != JsonValueKind.Null) return ParseOffer(std);
                if (game.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array && offers.GetArrayLength() > 0) return ParseOffer(offers[0]);
                
                // Final Fallback: Return the base game ID if no specific subscription offer was found
                if (!string.IsNullOrEmpty(fallbackId)) {
                    return new EaOffer { OfferId = fallbackId, DisplayName = slug };
                }
            }
        } catch { } 
        return null;
    }

    private EaOffer? ParseOffer(JsonElement offer) {
        var res = new EaOffer { OfferId = offer.TryGetProperty("id", out var oid) ? (oid.GetString() ?? "") : "", DisplayName = offer.TryGetProperty("name", out var dn) ? (dn.GetString() ?? "") : "" };
        if (offer.TryGetProperty("legacyOffer", out var legacy)) res.ContentId = legacy.TryGetProperty("contentId", out var cid) ? (cid.GetString() ?? "") : "";
        return string.IsNullOrEmpty(res.OfferId) ? null : res;
    }

    public async Task<List<EaOffer>> ResolveBatchOffersAsync(IEnumerable<string> offerIds)
    {
        var results = new List<EaOffer>();
        foreach (var id in offerIds.Distinct()) {
            var res = await ResolveCatalogOfferOnlyAsync(id);
            if (res != null) results.Add(res);
        }
        return results;
    }

    private async Task<EaOffer?> ResolveCatalogOfferOnlyAsync(string offerId)
    {
        var token = await GetAuthTokenAsync();
        var requestBody = new { query = "query getLegacyCatalogDefs($offerIds: [String!]!, $locale: Locale) { legacyOffers(offerIds: $offerIds, locale: $locale) { offerId: id contentId displayName } }", operationName = "getLegacyCatalogDefs", variables = new { locale = "DEFAULT", offerIds = new[] { offerId } } };
        try {
            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestBody)));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = content;
            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var legacyOffers = doc.RootElement.GetProperty("data").GetProperty("legacyOffers");
            if (legacyOffers.GetArrayLength() > 0) {
                var o = legacyOffers[0];
                return new EaOffer { OfferId = o.GetProperty("offerId").GetString() ?? "", ContentId = o.GetProperty("contentId").GetString() ?? "", DisplayName = o.GetProperty("displayName").GetString() ?? "" };
            }
        } catch { } 
        return null;
    }

    public async Task<string> GetEaPlaySubscriptionDetailsAsync() { return "EA Play Pro"; }

    public async Task<string?> GetAuthTokenAsync()
    {
        var tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens", "ea_token.txt");
        if (File.Exists(tokenPath)) return await File.ReadAllTextAsync(tokenPath);
        return null;
    }

    private void Log(string message) {
        try {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [EaDataService] {message}\n");
        } catch {{ }} 
    }
}