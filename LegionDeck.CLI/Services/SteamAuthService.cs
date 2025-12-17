using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using System.Collections.Generic;
using System.Net; // Added for HttpStatusCode and Cookie
using System.Net.Http; // Added for HttpClientHandler
using System.Net.Http.Headers; // Added for CookieContainer (though CookieContainer is in System.Net)
using System.Web; // Added for HttpUtility

namespace LegionDeck.CLI.Services;

public class SteamAuthService : IAuthService
{
    public Task<string?> LoginAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var form = new SteamLoginForm(tcs, null); 
                Application.Run(form);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    public async Task<bool> RefreshSessionAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Use the new silent parameter
                var form = new SteamLoginForm(tcs, null, silent: true); 
                Application.Run(form);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Wait for result with timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(15000)); // 15s timeout
        if (completedTask == tcs.Task)
        {
            try 
            {
                var result = await tcs.Task;
                return result == "SteamLoggedIn";
            }
            catch
            {
                return false;
            }
        }
        else
        {
             return false;
        }
    }

    public static async Task<(string pageContent, string finalUrl)> FetchProtectedPageAsync(string url, string cookieJsonPath)
    {
        // Attempt to fetch directly with HttpClient if cookies exist
        if (File.Exists(cookieJsonPath))
        {
            try
            {
                var cookieContainer = new CookieContainer();
                var handler = new HttpClientHandler { CookieContainer = cookieContainer };
                using var httpClient = new HttpClient(handler);
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"); // Use a consistent User-Agent matching WebView2

                // Load cookies from JSON and inject into CookieContainer
                var json = await File.ReadAllTextAsync(cookieJsonPath);
                var cookiesJson = JsonSerializer.Deserialize<List<JsonElement>>(json);
                
                if (cookiesJson != null)
                {
                    foreach (var c in cookiesJson)
                    {
                        try
                        {
                            var name = c.GetProperty("Name").GetString();
                            var value = c.GetProperty("Value").GetString();
                            var domain = c.GetProperty("Domain").GetString();
                            var path = c.GetProperty("Path").GetString() ?? "/";

                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(domain))
                            {
                                // Clean domain for CookieContainer.SetCookies
                                string cleanedDomain = domain.StartsWith(".") ? domain.Substring(1) : domain;
                                // URL-encode the cookie value to handle problematic characters like comma
                                string encodedValue = HttpUtility.UrlEncode(value);
                                cookieContainer.Add(new Uri($"https://{cleanedDomain}"), new Cookie(name, encodedValue, path, cleanedDomain));
                            }
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"[Warning] Failed to inject specific cookie from JSON: {innerEx.Message}");
                        }
                    }
                }

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await httpClient.SendAsync(request);
                
                // Check for redirects to login page or other non-successful status
                if (response.StatusCode == HttpStatusCode.OK && !response.RequestMessage!.RequestUri!.ToString().Contains("login"))
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // Basic check to see if content is likely what we expect (e.g., JSON for dynamicstore/userdata)
                    if (url.Contains("dynamicstore/userdata/") && content.Trim().StartsWith("{") || !url.Contains("dynamicstore/userdata/"))
                    {
                        Console.WriteLine("[Debug] Successfully fetched protected page directly with HttpClient.");
                        return (content, response.RequestMessage.RequestUri.ToString());
                    }
                }
                Console.WriteLine($"[Debug] HttpClient direct fetch failed or redirected to login for {url}. Status: {response.StatusCode}. Falling back to WebView2.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] HttpClient direct fetch failed: {ex.Message}. Falling back to WebView2.");
            }
        }

        // Fallback to WebView2 if HttpClient direct fetch fails or cookies don't exist
        var tcs = new TaskCompletionSource<(string, string)>();
        await Task.Run(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                var form = new SteamLoginForm(null, url, tcs, cookieJsonPath); 
                Application.Run(form);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return await tcs.Task;
    }
}

public class SteamLoginForm : Form
{
    private readonly TaskCompletionSource<string?>? _loginResultTcs;
    private readonly TaskCompletionSource<(string pageContent, string finalUrl)>? _fetchResultTcs;
    private readonly string _targetUrl;
    private readonly string? _cookieJsonPath; 
    private readonly bool _isSilent;
    private WebView2 _webView = new WebView2(); // Initialize here

    // Constructor for Login Mode
    public SteamLoginForm(TaskCompletionSource<string?> loginTcs, string? initialUrl, bool silent = false)
    {
        _loginResultTcs = loginTcs;
        _targetUrl = initialUrl ?? "https://steamcommunity.com/login/home/?goto=";
        _cookieJsonPath = null; 
        _isSilent = silent;
        InitializeComponentCommon();
        this.Text = "Steam Login - LegionDeck";
        
        if (_isSilent)
        {
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0;
        }
    }

    // Constructor for Fetch Mode
    public SteamLoginForm(TaskCompletionSource<string?>? loginTcs, string targetUrl, TaskCompletionSource<(string pageContent, string finalUrl)> fetchTcs, string cookieJsonPath) 
    {
        _loginResultTcs = loginTcs; 
        _targetUrl = targetUrl;
        _fetchResultTcs = fetchTcs;
        _cookieJsonPath = cookieJsonPath; 
        _isSilent = true;
        InitializeComponentCommon();
        this.Text = "LegionDeck - Fetching Data...";
        this.WindowState = FormWindowState.Minimized; // Revert to minimized
        this.ShowInTaskbar = false; // Revert to hidden
    }

    private void InitializeComponentCommon()
    {
        this.Width = 1024;
        this.Height = 768;
        this.StartPosition = FormStartPosition.CenterScreen;

        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += SteamLoginForm_Load;
    }

    private async void SteamLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            
            await _webView.EnsureCoreWebView2Async(env);

            // Set a consistent User-Agent for the WebView2 instance
            _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

            if (_cookieJsonPath != null && File.Exists(_cookieJsonPath)) // Explicit cookie injection
            {
                try 
                {
                    var json = await File.ReadAllTextAsync(_cookieJsonPath);
                    var cookies = JsonSerializer.Deserialize<List<JsonElement>>(json);
                    
                    if (cookies != null)
                    {
                        var cookieManager = _webView.CoreWebView2.CookieManager;
                        foreach (var c in cookies)
                        {
                            try
                            {
                                var name = c.GetProperty("Name").GetString();
                                var value = c.GetProperty("Value").GetString();
                                var domain = c.GetProperty("Domain").GetString();
                                var path = c.GetProperty("Path").GetString() ?? "/";
                                
                                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(domain))
                                {
                                    CoreWebView2Cookie cookie = cookieManager.CreateCookie(name, value, domain, path);
                                    if (c.TryGetProperty("IsSecure", out var secure)) cookie.IsSecure = secure.GetBoolean();
                                    if (c.TryGetProperty("IsHttpOnly", out var httpOnly)) cookie.IsHttpOnly = httpOnly.GetBoolean();
                                    if (c.TryGetProperty("Expires", out var expires) && expires.ValueKind == JsonValueKind.Number) 
                                    {
                                        cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(expires.GetInt64()).DateTime;
                                    }

                                    cookieManager.AddOrUpdateCookie(cookie);
                                }
                            }
                            catch (Exception innerEx)
                            {
                                Console.WriteLine($"[Warning] Failed to inject specific cookie '{c.GetProperty("Name").GetString()}': {innerEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] Failed to parse/load cookies from JSON: {ex.Message}");
                }
            }

            if (_fetchResultTcs == null) // Login Mode
            {
                // Clear old cookies to ensure a clean login, only for INTERACTIVE Login mode.
                // If Silent (Refresh), we want to KEEP cookies to try and auto-login.
                if (!_isSilent)
                {
                    var cookieManager = _webView.CoreWebView2.CookieManager;
                    cookieManager.DeleteCookiesWithDomainAndPath("steamcommunity.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath(".steamcommunity.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath("steampowered.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath(".steampowered.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath("store.steampowered.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath(".store.steampowered.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath("help.steampowered.com", "/", null);
                    cookieManager.DeleteCookiesWithDomainAndPath("login.steampowered.com", "/", null);
                }
            }
            
            
            // Initial navigation based on mode
            if (_fetchResultTcs != null && _targetUrl.Contains("dynamicstore/userdata/"))
            {
                _webView.Source = new Uri("https://store.steampowered.com/"); // First navigate to main store page
            }
            else
            {
                _webView.Source = new Uri(_targetUrl!); 
            }

            _webView.NavigationCompleted += WebView_NavigationCompleted;
        }
        catch (Exception ex)
        {
            _loginResultTcs?.TrySetException(ex);
            _fetchResultTcs?.TrySetException(ex);
            this.Close();
        }
    }

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Console.WriteLine($"[Error] WebView2 Navigation Failed to: {_webView!.Source}, Status: {e.WebErrorStatus}");
            _loginResultTcs?.TrySetException(new Exception($"Navigation failed: {_webView!.Source}")); 
            _fetchResultTcs?.TrySetException(new Exception($"Navigation failed: {_webView!.Source}")); 
            this.Close();
            return;
        }

        // Logic for two-step navigation in Fetch Mode when targeting dynamicstore/userdata/
        if (_fetchResultTcs != null && _targetUrl.Contains("dynamicstore/userdata/") && _webView.Source.ToString() == "https://store.steampowered.com/")
        {
            // After navigating to main store page, wait for it to load then go to dynamicstore/userdata/
            var maxAttemptsStore = 10;
            for (int i = 0; i < maxAttemptsStore; i++)
            {
                var readyState = await _webView.ExecuteScriptAsync("document.readyState");
                var unescapedReadyState = JsonSerializer.Deserialize<string>(readyState);
                if (unescapedReadyState == "complete")
                {
                    await Task.Delay(2000); // Give a bit more time after ready state
                    break;
                }
                await Task.Delay(1000); 
            }
            _webView.Source = new Uri(_targetUrl); // Now navigate to the final target URL
            return; // Wait for the next NavigationCompleted event for the final URL
        }

        if (_fetchResultTcs != null) // Fetch Mode (after potential two-step navigation)
        {
            // Give JS time to render and for API calls to settle, and wait for document to be complete
            await Task.Delay(5000); 
            
            // Wait until document is ready
            var maxAttempts = 10;
            for (int i = 0; i < maxAttempts; i++)
            {
                var readyState = await _webView.ExecuteScriptAsync("document.readyState");
                var unescapedReadyState = JsonSerializer.Deserialize<string>(readyState);
                if (unescapedReadyState == "complete")
                {
                    break;
                }
                await Task.Delay(1000); 
            }

            try 
            {
                string content;
                if (_webView!.Source.ToString().Contains("/dynamicstore/userdata/")) 
                {
                    // For JSON API, get innerText to avoid HTML wrapping
                    content = await _webView.ExecuteScriptAsync("document.body.innerText");
                }
                else
                {
                    content = await _webView.ExecuteScriptAsync("document.documentElement.outerHTML");
                }
                var unescaped = JsonSerializer.Deserialize<string>(content);
                _fetchResultTcs.TrySetResult((unescaped ?? string.Empty, _webView.Source.ToString()));
            }
            catch (Exception ex)
            {
                _fetchResultTcs?.TrySetException(ex);
            }
            this.Close();
        }
        else if (_loginResultTcs != null) // Login Mode
        {
            var cookieManager = _webView!.CoreWebView2.CookieManager; 
            
            // Check if we are already on store.steampowered.com (Stage 2)
            if (_webView.Source.Host.Contains("store.steampowered.com"))
            {
                // Stage 2.5: Navigate to dynamicstore/userdata to ensure session is fully established for this API
                if (!_webView.Source.ToString().Contains("/dynamicstore/userdata/"))
                {
                    Console.WriteLine($"[Auth Stage 2.5] Navigating to dynamicstore/userdata/ to finalize session.");
                    await Task.Delay(5000); // Wait for 5 seconds to ensure all cookies are set
                    _webView.Source = new Uri("https://store.steampowered.com/dynamicstore/userdata/");
                    return; // Wait for next NavigationCompleted event
                }

                // Stage 3: We are on dynamicstore/userdata/, session should be complete
                Console.WriteLine($"[Auth Stage 3] Session fully established on dynamicstore/userdata/.");

                var communityCookies = await cookieManager.GetCookiesAsync("https://steamcommunity.com");
                var storeCookies = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                
                var allCookies = communityCookies.Concat(storeCookies)
                                                 .GroupBy(c => c.Name + c.Domain) 
                                                 .Select(g => g.First())
                                                 .ToList();

                if (allCookies.Any(c => c.Name == "steamLoginSecure"))
                {
                    Console.WriteLine($"[Auth Success] Retrieved steamLoginSecure and synced to Store.");
                    
                    var cookieData = allCookies.Select(c => new 
                    { 
                        c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                    }).ToList();

                    var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });

                    var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                    Directory.CreateDirectory(authTokensPath);
                    await File.WriteAllTextAsync(Path.Combine(authTokensPath, "steam_cookies.json"), json);
                    Console.WriteLine($"Steam cookies saved to: {Path.Combine(authTokensPath, "steam_cookies.json")}");

                    _loginResultTcs.TrySetResult("SteamLoggedIn");
                    this.Close();
                }
                return;
            }

            // Stage 1: Check steamcommunity
            var cookies = await cookieManager.GetCookiesAsync("https://steamcommunity.com");
            var loginCookie = cookies.FirstOrDefault(c => c.Name == "steamLoginSecure");
            
            if (loginCookie != null)
            {
                Console.WriteLine($"[Auth Stage 1] Logged in on Community. Extracting and Saving Cookies.");
                
                // Retrieve all cookies from both domains after successful login on community
                var communityCookies = await cookieManager.GetCookiesAsync("https://steamcommunity.com");
                var storeCookies = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                
                var allCookies = communityCookies.Concat(storeCookies)
                                                 .GroupBy(c => c.Name + c.Domain) 
                                                 .Select(g => g.First())
                                                 .ToList();

                var cookieData = allCookies.Select(c => new 
                { 
                    c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                }).ToList();

                var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });

                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                Directory.CreateDirectory(authTokensPath);
                await File.WriteAllTextAsync(Path.Combine(authTokensPath, "steam_cookies.json"), json);
                Console.WriteLine($"Steam cookies saved to: {Path.Combine(authTokensPath, "steam_cookies.json")}");

                _loginResultTcs?.TrySetResult("SteamLoggedIn");
                this.Close();
                return; // Exit after successful login
            }
            else if (_isSilent)
            {
                 // If silent and no login cookie on community, we are likely not logged in.
                 Console.WriteLine("[Silent Refresh] steamLoginSecure not found on Community. Session invalid.");
                 _loginResultTcs?.TrySetResult("NeedsLogin");
                 this.Close();
                 return;
            }

            // If not logged in on community, continue with navigation to store to ensure session is fully established for this API
            if (_webView.Source.Host.Contains("store.steampowered.com"))
            {
                 // Stage 2.5: Navigate to dynamicstore/userdata to ensure session is fully established for this API
                if (!_webView.Source.ToString().Contains("/dynamicstore/userdata/"))
                {
                    Console.WriteLine($"[Auth Stage 2.5] Navigating to dynamicstore/userdata/ to finalize session.");
                    await Task.Delay(5000); // Wait for 5 seconds to ensure all cookies are set
                    _webView.Source = new Uri("https://store.steampowered.com/dynamicstore/userdata/");
                    return; // Wait for next NavigationCompleted event
                }

                // Stage 3: We are on dynamicstore/userdata/, session should be complete
                Console.WriteLine($"[Auth Stage 3] Session fully established on dynamicstore/userdata/.");

                // If _loginResultTcs is still active and we reach here, it means we didn't find steamLoginSecure on community,
                // but perhaps the user logged in directly on store.steampowered.com or another path
                var allStoreCookies = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                if (allStoreCookies.Any(c => c.Name == "steamLoginSecure"))
                {
                    Console.WriteLine($"[Auth Success] Retrieved steamLoginSecure from Store.");
                    
                    var cookieData = allStoreCookies.Select(c => new 
                    { 
                        c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                    }).ToList();

                    var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });

                    var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                    Directory.CreateDirectory(authTokensPath);
                    await File.WriteAllTextAsync(Path.Combine(authTokensPath, "steam_cookies.json"), json);
                    Console.WriteLine($"Steam cookies saved to: {Path.Combine(authTokensPath, "steam_cookies.json")}");

                    _loginResultTcs?.TrySetResult("SteamLoggedIn");
                    this.Close();
                }
            }
        }
    }
}
