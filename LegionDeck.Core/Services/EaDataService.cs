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

        string? nextCursor = "0"; // Start with offset 0
        int limit = 50; // Increased batch size

        do
        {
            var variables = new
            {
                isMac = false,
                addFieldsToPreloadGames = true,
                locale = "en",
                limit = limit,
                next = nextCursor,
                type = new[] { "DIGITAL_FULL_GAME", "PACKAGED_FULL_GAME" },
                entitlementEnabled = true,
                storefronts = new[] { "EA", "STEAM", "EPIC" },
                ownershipMethods = new[]
                {
                    "UNKNOWN", "ASSOCIATION", "PURCHASE", "REDEMPTION", "GIFT_RECEIPT", "ENTITLEMENT_GRANT", 
                    "DIRECT_ENTITLEMENT", "PRE_ORDER_PURCHASE", "VAULT", "XGP_VAULT", "STEAM", "STEAM_VAULT", 
                    "STEAM_SUBSCRIPTION", "EPIC", "EPIC_VAULT", "EPIC_SUBSCRIPTION"
                },
                platforms = new[] { "PC" }
            };

            // URL-encode variables for GET request (Persisted Queries use GET)
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

                Log($"[Juno] Raw Response (Cursor: {nextCursor}): {json.Substring(0, Math.Min(json.Length, 200))}");

                using var doc = JsonDocument.Parse(json);
                
                // Auth Check
                if (doc.RootElement.TryGetProperty("errors", out var errors))
                {
                    if (errors.GetRawText().Contains("UNAUTHENTICATED") || errors.GetRawText().Contains("Not authenticated"))
                    {
                        Log("[Juno] Token expired during pagination. Refreshing...");
                        var authService = new EaAuthService();
                        if (await authService.RefreshTokenAsync())
                        {
                            token = await GetAuthTokenAsync();
                            continue; // Retry this page
                        }
                        else return results; // Refresh failed
                    }
                }

                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.TryGetProperty("me", out var me) && 
                    me.TryGetProperty("ownedGameProducts", out var owned))
                {
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
                            }
                        }
                    }

                    // Handle Pagination
                    if (owned.TryGetProperty("next", out var nextElem) && nextElem.ValueKind == JsonValueKind.String)
                    {
                        nextCursor = nextElem.GetString();
                    }
                    else
                    {
                        nextCursor = null; // No more pages
                    }
                }
                else
                {
                    nextCursor = null; // Unexpected structure
                }
            }
            catch (Exception ex) 
            { 
                Log($"Error in GetVaultOffersAsync: {ex.Message}");
                nextCursor = null; 
            }

        } while (!string.IsNullOrEmpty(nextCursor));

        Log($"[Juno] Successfully retrieved {results.Count} games from Vault (Paginated).");
        return results;
    }

                public async Task<List<EaOffer>> ResolveBatchOffersAsync(IEnumerable<string> offerIds)

                {

                    var results = new List<EaOffer>();

                    var distinctIds = offerIds.Distinct().ToList();

                    var token = await GetAuthTokenAsync();

                    

                    if (string.IsNullOrEmpty(token)) return results;

            

                    // Chunking by 20 to be safe

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

            

                        var requestBody = new

                        {

                            query = query,

                            operationName = "getLegacyCatalogDefs",

                            variables = new { locale = "DEFAULT", offerIds = batch }

                        };

            

                        try

                        {

                            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);

                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                            

                            // IMPORTANT: Use MediaTypeHeaderValue to avoid charset being added automatically

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

                    // ... (Keep existing implementation but use the new Content-Type fix if updating this method too)

                    // For now, we will rely on ResolveBatchOffersAsync in the update service.

                    return (await ResolveBatchOffersAsync(new[] { slug })).FirstOrDefault();

                }    private async Task<EaOffer?> ResolveCatalogOfferOnlyAsync(string offerId)
    {
        var query = @"
        query getLegacyCatalogDefs($offerIds: [String!]!, $locale: Locale) {
          legacyOffers(offerIds: $offerIds, locale: $locale) {
            offerId: id
            contentId
            displayName
          }
        }";

        var requestBody = new
        {
            query = query,
            operationName = "getLegacyCatalogDefs",
            variables = new { locale = "DEFAULT", offerIds = new[] { offerId } }
        };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);
            var token = await GetAuthTokenAsync();
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null &&
                data.TryGetProperty("legacyOffers", out var legacyOffers) &&
                legacyOffers.GetArrayLength() > 0)
            {
                var offer = legacyOffers[0];
                if (offer.ValueKind == JsonValueKind.Null) return null;
                return new EaOffer {
                    OfferId = offer.TryGetProperty("offerId", out var oid) ? (oid.GetString() ?? "") : "",
                    ContentId = offer.TryGetProperty("contentId", out var cid) ? (cid.GetString() ?? "") : "",
                    DisplayName = offer.TryGetProperty("displayName", out var dn) ? (dn.GetString() ?? "") : ""
                };
            }
        } catch { }
        return null;
    }

    // Keep for backward compatibility if needed, but ResolveOfferAsync is preferred
    public async Task<string?> ResolveContentIdAsync(string slug)
    {
        var offer = await ResolveOfferAsync(slug);
        return offer?.ContentId;
    }

    public async Task<string> GetEaPlaySubscriptionDetailsAsync()
    {
        if (!File.Exists(_eaCookieFilePath))
        {
            return "Error: No Cookies";
        }

        var subscriptionCheckUrl = "https://www.ea.com/ea-play/member-benefits"; 

        try
        {
            var (pageContent, finalUrl) = await SteamAuthService.FetchProtectedPageAsync(subscriptionCheckUrl, _eaCookieFilePath);

            if (pageContent.Contains("EA Play Pro", StringComparison.OrdinalIgnoreCase))
            {
                return "EA Play Pro";
            }
            else if (pageContent.Contains("EA Play", StringComparison.OrdinalIgnoreCase))
            {
                return "EA Play";
            }
            else
            {
                return "None";
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to check EA Play subscription: {ex.Message}");
            return "Error: Check Failed";
        }
    }

    public async Task<string?> GetAuthTokenAsync()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var tokenPath = Path.Combine(authTokensPath, "ea_token.txt");

        if (File.Exists(tokenPath))
        {
            return await File.ReadAllTextAsync(tokenPath);
        }

        if (!File.Exists(_eaCookieFilePath)) return null;

        try
        {
            // Fallback to SID cookie if the specific token file doesn't exist
            var cookiesJson = await File.ReadAllTextAsync(_eaCookieFilePath);
            var cookies = JsonSerializer.Deserialize<List<CookieModel>>(cookiesJson);
            var sid = cookies?.FirstOrDefault(c => c.Name == "sid")?.Value;
            return sid;
        }
        catch { return null; }
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

    private class CookieModel
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }
}