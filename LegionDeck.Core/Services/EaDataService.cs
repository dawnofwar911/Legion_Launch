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
                    int limit = 500; 
            
                    do
                    {
                        var variables = new
                        {
                            isMac = false,
                            addFieldsToPreloadGames = true,
                            locale = "en",
                            limit = limit,
                            next = nextCursor,
                            type = new[] { "DIGITAL_FULL_GAME", "PACKAGED_FULL_GAME", "BUNDLE" },
                            entitlementEnabled = true,
                            storefronts = new[] { "EA", "STEAM", "EPIC" },
                            ownershipMethods = new[]
                            {
                                "UNKNOWN", "ASSOCIATION", "PURCHASE", "REDEMPTION", "GIFT_RECEIPT", "ENTITLEMENT_GRANT", 
                                "DIRECT_ENTITLEMENT", "PRE_ORDER_PURCHASE", "VAULT", "XGP_VAULT", "STEAM", "STEAM_VAULT", 
                                "STEAM_SUBSCRIPTION", "EPIC", "EPIC_VAULT", "EPIC_SUBSCRIPTION", "PSN_SUBSCRIPTION", "XBL_SUBSCRIPTION", "XGP_SUBSCRIPTION"
                            },
                            platforms = new[] { "PC" }
                        };    
                var variablesJson = JsonSerializer.Serialize(variables);
                var extensionsJson = JsonSerializer.Serialize(new 
                {
                    persistedQuery = new 
                    {
                        version = 1, 
                        sha256Hash = "5de4178ee7e1f084ce9deca856c74a9e03547a67dfafc0cb844d532fb54ae73d" 
                    } 
                });
    
                var uri = $"{GraphQlUrl}?operationName=getPreloadedOwnedGames&variables={Uri.EscapeDataString(variablesJson)}&extensions={Uri.EscapeDataString(extensionsJson)}";
    
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
                    var response = await _httpClient.SendAsync(request);
                    var json = await response.Content.ReadAsStringAsync();
    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || 
                        json.Contains("UNAUTHENTICATED") || json.Contains("Not authenticated"))
                    {
                        Log("[Juno] Auth failed. Attempting silent refresh...");
                        var authService = new EaAuthService();
                        if (await authService.RefreshTokenAsync())
                        {
                            token = await GetAuthTokenAsync();
                            continue; 
                        }
                        else return results;
                    }
    
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null &&
                        data.TryGetProperty("me", out var me) && me.ValueKind != JsonValueKind.Null &&
                        me.TryGetProperty("ownedGameProducts", out var owned) && owned.ValueKind != JsonValueKind.Null)
                    {
                        int pageCount = 0;
                        if (owned.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in items.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.Null) continue;
                                string offerId = item.TryGetProperty("originOfferId", out var oid) ? (oid.GetString() ?? "") : "";
                                string displayName = "";
                                if (item.TryGetProperty("product", out var product) && product.ValueKind != JsonValueKind.Null)
                                {
                                    displayName = product.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                                }
    
                                if (!string.IsNullOrEmpty(offerId))
                                {
                                    results.Add(new EaOffer { OfferId = offerId, DisplayName = displayName });
                                    pageCount++;
                                }
                            }
                        }
    
                        Log($"[Juno] Page received {pageCount} items. Total so far: {results.Count}");
    
                        if (owned.TryGetProperty("next", out var nextElem) && nextElem.ValueKind == JsonValueKind.String)
                        {
                            var newCursor = nextElem.GetString();
                            if (string.IsNullOrEmpty(newCursor) || newCursor == "0" || newCursor == nextCursor)
                                nextCursor = null;
                            else 
                                nextCursor = newCursor;
                        }
                        else nextCursor = null;
                    }
                    else
                    {
                        Log($"[Juno] Unexpected structure. Response: {json.Substring(0, Math.Min(json.Length, 500))}");
                        nextCursor = null;
                    }
                }
                catch (Exception ex) { Log($"Error in GetVaultOffersAsync: {ex.Message}"); nextCursor = null; }
    
            } while (!string.IsNullOrEmpty(nextCursor));
    
            return results;
        }
    public async Task<List<EaOffer>> ResolveBatchOffersAsync(IEnumerable<string> offerIds)
    {
        var results = new List<EaOffer>();
        var distinctIds = offerIds.Distinct().ToList();
        var token = await GetAuthTokenAsync();
        if (string.IsNullOrEmpty(token)) return results;

        for (int i = 0; i < distinctIds.Count; i += 20)
        {
            var batch = distinctIds.Skip(i).Take(20).ToArray();
            var query = @"
            query getLegacyCatalogDefs($offerIds: [String!]!, $locale: Locale) {
              legacyOffers(offerIds: $offerIds, locale: $locale) {
                offerId: id
                contentId
                displayName
              }
            }";

            var requestBody = new { query, operationName = "getLegacyCatalogDefs", variables = new { locale = "DEFAULT", offerIds = batch } };

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestBody)));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.TryGetProperty("legacyOffers", out var legacyOffers))
                {
                    foreach (var offer in legacyOffers.EnumerateArray())
                    {
                        if (offer.ValueKind == JsonValueKind.Null) continue;
                        results.Add(new EaOffer
                        {
                            OfferId = offer.TryGetProperty("offerId", out var oid) ? (oid.GetString() ?? "") : "",
                            ContentId = offer.TryGetProperty("contentId", out var cid) ? (cid.GetString() ?? "") : "",
                            DisplayName = offer.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? "") : ""
                        });
                    }
                }
            }
            catch (Exception ex) { Log($"Batch resolution error: {ex.Message}"); }
        }
        return results;
    }

    public async Task<EaOffer?> ResolveOfferAsync(string slug)
    {
        var offers = await ResolveBatchOffersAsync(new[] { slug });
        return offers.FirstOrDefault();
    }

    public async Task<string> GetEaPlaySubscriptionDetailsAsync()
    {
        var token = await GetAuthTokenAsync();
        if (string.IsNullOrEmpty(token)) return "Error: No Token";

        var extensionsJson = JsonSerializer.Serialize(new 
        { 
            persistedQuery = new 
            { 
                version = 1, 
                sha256Hash = "d127c63383688258dd6133009a12668a2f3d1a6d47c4927d00fa84a398205a88" 
            } 
        });

        var uri = $"{GraphQlUrl}?operationName=GetUserSubscription&variables=%7B%7D&extensions={Uri.EscapeDataString(extensionsJson)}";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && 
                data.TryGetProperty("me", out var me) && 
                me.TryGetProperty("subscription", out var sub))
            {
                var level = sub.TryGetProperty("level", out var l) ? l.GetString() : "NONE";
                var status = sub.TryGetProperty("status", out var s) ? s.GetString() : "NONE";

                if (status == "ACTIVE")
                {
                    return level == "PREMIUM" ? "EA Play Pro" : "EA Play";
                }
            }
            return "None";
        }
        catch (Exception ex)
        {
            Log($"Failed to check subscription: {ex.Message}");
            return "Error: Check Failed";
        }
    }

    public async Task<string?> GetAuthTokenAsync()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var tokenPath = Path.Combine(authTokensPath, "ea_token.txt");
        if (File.Exists(tokenPath)) return await File.ReadAllTextAsync(tokenPath);
        return null;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [EaDataService] {message}\n");
        }
        catch {{ }}
    }
}
