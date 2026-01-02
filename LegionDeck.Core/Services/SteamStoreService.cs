using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace LegionDeck.Core.Services;

public class SteamStoreService
{
    private readonly HttpClient _httpClient;

    public SteamStoreService()
    {
        var handler = new HttpClientHandler();
        
        try
        {
            var authTokensPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var cookiePath = System.IO.Path.Combine(authTokensPath, "steam_cookies.json");
            
            if (System.IO.File.Exists(cookiePath))
            {
                var cookieContainer = new System.Net.CookieContainer();
                var json = System.IO.File.ReadAllText(cookiePath);
                using var doc = JsonDocument.Parse(json);
                
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("Name", out var name) && 
                        element.TryGetProperty("Value", out var value) && 
                        element.TryGetProperty("Domain", out var domain))
                    {
                        var d = domain.GetString();
                        // Fix for domain needing to be without leading dot for URI, but with dot for Cookie
                        // Actually CookieContainer is smart.
                        if (!string.IsNullOrEmpty(d))
                        {
                            try 
                            {
                                cookieContainer.Add(new Uri($"https://store.steampowered.com"), new System.Net.Cookie(name.GetString(), value.GetString(), "/", d));
                            } catch {}
                        }
                    }
                }
                handler.CookieContainer = cookieContainer;
            }
        }
        catch { }

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://store.steampowered.com/");
    }

    public class SteamStoreDetails
    {
        public string? Name { get; set; }
        public string? HeaderImage { get; set; } // Landscape
        public string? VerticalCover { get; set; } // Vertical (from SGDB or Steam Library assets)
        public string? ShortDescription { get; set; }
        public string? Type { get; set; }
    }

    public async Task<System.Collections.Generic.Dictionary<int, SteamStoreDetails>> GetStoreDetailsBatchAsync(System.Collections.Generic.List<int> appIds)
    {
        var results = new System.Collections.Generic.Dictionary<int, SteamStoreDetails>();
        if (appIds == null || appIds.Count == 0) return results;

        var ids = string.Join(",", appIds);
        // Remove filters to ensure we get 'type', 'header_image', etc.
        // Removed trailing slash after appdetails
        var url = $"https://store.steampowered.com/api/appdetails?appids={ids}&cc=us&l=en"; 
        
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            
            foreach (var appId in appIds)
            {
                if (doc.RootElement.TryGetProperty(appId.ToString(), out var appElement) &&
                    appElement.TryGetProperty("success", out var successElement) &&
                    successElement.GetBoolean() == true &&
                    appElement.TryGetProperty("data", out var dataElement))
                {
                    var details = new SteamStoreDetails();
                    
                    if (dataElement.TryGetProperty("name", out var nameElement))
                        details.Name = nameElement.GetString() ?? string.Empty;
                    
                    if (dataElement.TryGetProperty("type", out var typeElement))
                        details.Type = typeElement.GetString() ?? string.Empty;

                    if (dataElement.TryGetProperty("short_description", out var descElement))
                        details.ShortDescription = StripHtml(descElement.GetString() ?? string.Empty);

                    if (dataElement.TryGetProperty("header_image", out var imgElement))
                        details.HeaderImage = imgElement.GetString() ?? string.Empty;

                    results[appId] = details;
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamStoreService] Error fetching batch details: {ex.Message}\n");
            } catch {}
        }
        return results;
    }

    public async Task<System.Collections.Generic.Dictionary<int, string>> GetFullAppListAsync(string? apiKey = null)
    {
        var results = new System.Collections.Generic.Dictionary<int, string>();
        
        // Use a clean HttpClient for the public API to avoid header/cookie conflicts
        using var apiHttpClient = new HttpClient();
        apiHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var urls = new List<string>();
        
        // Priority 1: IStoreService (Reliable, needs Key)
        if (!string.IsNullOrEmpty(apiKey))
        {
            urls.Add($"https://api.steampowered.com/IStoreService/GetAppList/v1/?key={apiKey}&include_games=true&include_dlc=true&include_software=true");
        }

        // Priority 2: ISteamApps (Public, but flaky/deprecated)
        urls.Add("https://api.steampowered.com/ISteamApps/GetAppList/v2/");
        urls.Add("https://api.steampowered.com/ISteamApps/GetAppList/v2"); 
        urls.Add("https://api.steampowered.com/ISteamApps/GetAppList/v0002/?format=json");

        string response = string.Empty;
        Exception? lastEx = null;

        foreach (var url in urls)
        {
            try
            {
                response = await apiHttpClient.GetStringAsync(url);
                if (!string.IsNullOrEmpty(response)) break;
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        if (string.IsNullOrEmpty(response))
        {
             try
             {
                 var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
                 System.IO.File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamStoreService] All AppList endpoints failed. Last error: {lastEx?.Message}\n");
             } catch {}
             return results;
        }

        try
        {
            using var doc = JsonDocument.Parse(response);
            // Handle IStoreService format (response -> apps) or ISteamApps format (applist -> apps)
            
            JsonElement appsArray = default;
            
            if (doc.RootElement.TryGetProperty("response", out var responseRoot) && responseRoot.TryGetProperty("apps", out var storeApps))
            {
                appsArray = storeApps;
            }
            else if (doc.RootElement.TryGetProperty("applist", out var applist) && applist.TryGetProperty("apps", out var legacyApps))
            {
                appsArray = legacyApps;
            }

            if (appsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var app in appsArray.EnumerateArray())
                {
                    if (app.TryGetProperty("appid", out var id) && app.TryGetProperty("name", out var name))
                    {
                        var nameStr = name.GetString();
                        if (!string.IsNullOrEmpty(nameStr))
                        {
                            results[id.GetInt32()] = nameStr;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamStoreService] Error parsing app list: {ex.Message}\n");
            } catch {}
        }
        return results;
    }

    public async Task<SteamStoreDetails?> GetStoreDetailsAsync(int appId)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us&l=en";
        // Let exceptions propagate so GameEnrichmentService can handle rate limits (429/403)
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        
        if (doc.RootElement.TryGetProperty(appId.ToString(), out var appElement) &&
            appElement.TryGetProperty("success", out var successElement) &&
            successElement.GetBoolean() == true &&
            appElement.TryGetProperty("data", out var dataElement))
        {
            var details = new SteamStoreDetails();
            
            if (dataElement.TryGetProperty("name", out var nameElement))
                details.Name = nameElement.GetString() ?? string.Empty;
            
            if (dataElement.TryGetProperty("short_description", out var descElement))
                details.ShortDescription = StripHtml(descElement.GetString() ?? string.Empty);

            if (dataElement.TryGetProperty("header_image", out var imgElement))
                details.HeaderImage = imgElement.GetString() ?? string.Empty;
            
            if (dataElement.TryGetProperty("type", out var typeElement))
                details.Type = typeElement.GetString() ?? string.Empty;

            return details;
        }
        return null;
    }

    private string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        
        // Decode HTML entities
        var decoded = System.Net.WebUtility.HtmlDecode(input);
        
        // Remove tags
        return Regex.Replace(decoded, "<.*?>", string.Empty);
    }
}
