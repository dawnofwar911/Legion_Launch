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
using System.Net; 
using System.Net.Http; 
using System.Net.Http.Headers; 
using System.Web; 

namespace LegionDeck.Core.Services;

public class SteamAuthService : IAuthService
{
    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamAuthService] {message}\n");
        }
        catch { }
    }

    public void ClearCookies()
    {
        try
        {
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var cookiePath = Path.Combine(authTokensPath, "steam_cookies.json");
            if (File.Exists(cookiePath))
            {
                File.Delete(cookiePath);
                Log($"Deleted invalid/expired cookies at {cookiePath}");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to clear cookies: {ex.Message}");
        }
    }

    public Task<string?> LoginAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        var thread = new Thread(() =>
        {
            try
            {
                Log("Starting interactive LoginAsync thread");
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var form = new SteamLoginForm(tcs, null); 
                Application.Run(form);
                Log("Interactive login form closed");
            }
            catch (Exception ex)
            {
                Log($"LoginAsync thread exception: {ex.Message}");
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
                Log("Starting silent RefreshSessionAsync thread");
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Use the new silent parameter
                var form = new SteamLoginForm(tcs, null, silent: true); 
                Application.Run(form);
                Log("Silent refresh form closed");
            }
            catch (Exception ex)
            {
                Log($"RefreshSessionAsync thread exception: {ex.Message}");
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
                Log($"RefreshSessionAsync result: {result}");
                return result == "SteamLoggedIn";
            }
            catch (Exception ex)
            {
                Log($"RefreshSessionAsync task exception: {ex.Message}");
                return false;
            }
        }
        else
        {
             Log("RefreshSessionAsync timed out");
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
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"); 

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
                                
                                // REVERT: Do NOT URL-encode. Pass raw value.
                                // Common issue: Cookie values often contain encoded chars already. Double encoding breaks them.
                                cookieContainer.Add(new Uri($"https://{cleanedDomain}"), new Cookie(name, value, path, cleanedDomain));
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
        var thread = new Thread(() =>
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
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
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
    private WebView2 _webView = new WebView2(); 

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
        this.WindowState = FormWindowState.Minimized; 
        this.ShowInTaskbar = false; 
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [SteamLoginForm] {message}\n");
        }
        catch {{ }}
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
            Log("SteamLoginForm_Load started");
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2");
            Log($"Creating WebView2 environment with UserDataFolder: {userDataFolder}");
            
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
            Log("WebView2 initialized successfully");

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
                                Log($"[Warning] Failed to inject specific cookie '{{c.GetProperty(\"Name\").GetString()}}': {innerEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Warning] Failed to parse/load cookies from JSON: {ex.Message}");
                }
            }

            if (_fetchResultTcs == null) // Login Mode
            {
                // Clear old cookies to ensure a clean login, only for INTERACTIVE Login mode.
                // If Silent (Refresh), we want to KEEP cookies to try and auto-login.
                if (!_isSilent)
                {
                    Log("Interactive Login: Clearing old cookies to force login prompt.");
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
                Log("Navigating to store root for dynamicstore fetch...");
                _webView.Source = new Uri("https://store.steampowered.com/"); 
            }
            else
            {
                Log($"Navigating to target URL: {_targetUrl}");
                _webView.Source = new Uri(_targetUrl!); 
            }

            _webView.NavigationCompleted += WebView_NavigationCompleted;
        }
        catch (Exception ex)
        {
            Log($"SteamLoginForm_Load failed: {ex.Message}");
            _loginResultTcs?.TrySetException(ex);
            _fetchResultTcs?.TrySetException(ex);
            this.Close();
        }
    }

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Log($"NavigationCompleted. IsSuccess: {e.IsSuccess}, Status: {e.WebErrorStatus}, Source: {_webView.Source}");
        
        if (!e.IsSuccess)
        {
            Log($"[Error] WebView2 Navigation Failed to: {{_webView!.Source}}, Status: {e.WebErrorStatus}");
            _loginResultTcs?.TrySetException(new Exception($"Navigation failed: {{_webView!.Source}} (Status: {e.WebErrorStatus})"));   
            _fetchResultTcs?.TrySetException(new Exception($"Navigation failed: {{_webView!.Source}} (Status: {e.WebErrorStatus})"));   
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
                    await Task.Delay(2000); 
                    break;
                }
                await Task.Delay(1000); 
            }
            _webView.Source = new Uri(_targetUrl); 
            return; 
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
                    Log("[Auth Stage 2.5] Navigating to dynamicstore/userdata/ to finalize session.");
                    await Task.Delay(5000); 
                    _webView.Source = new Uri("https://store.steampowered.com/dynamicstore/userdata/");  
                    return; 
                }

                // Stage 3: We are on dynamicstore/userdata/, session should be complete
                Log("[Auth Stage 3] Session fully established on dynamicstore/userdata/.");

                var communityCookies = await cookieManager.GetCookiesAsync("https://steamcommunity.com");
                var storeCookies = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                
                var allCookies = communityCookies.Concat(storeCookies)
                                                 .GroupBy(c => c.Name + c.Domain) 
                                                 .Select(g => g.First())
                                                 .ToList();

                if (allCookies.Any(c => c.Name == "steamLoginSecure"))
                {
                    Log("[Auth Success] Retrieved steamLoginSecure and synced to Store.");
                    
                    var cookieData = allCookies.Select(c => new 
                    { 
                        c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                    }).ToList();

                    var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });

                    var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                    Directory.CreateDirectory(authTokensPath);
                    await File.WriteAllTextAsync(Path.Combine(authTokensPath, "steam_cookies.json"), json);
                    Log($"Steam cookies saved to: {Path.Combine(authTokensPath, "steam_cookies.json")}");

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
                Log("[Auth Stage 1] Found steamLoginSecure on Community.");
                
                // Check if Store also has the cookie
                var storeCookiesCheck = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                if (!storeCookiesCheck.Any(c => c.Name == "steamLoginSecure"))
                {
                    Log("[Auth Stage 1.5] Store domain missing steamLoginSecure. Navigating to Store to sync session.");
                    // Force navigation to Store login check to sync cookies
                    _webView.Source = new Uri("https://store.steampowered.com/login/checkstoredlogin/?redirectURL=0");
                    return;
                }

                Log("[Auth Stage 1] Session valid on both domains. Extracting and Saving Cookies.");
                
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
                Log($"Steam cookies saved to: {Path.Combine(authTokensPath, "steam_cookies.json")}");

                _loginResultTcs?.TrySetResult("SteamLoggedIn");
                this.Close();
                return; 
            }
            else if (_isSilent)
            {
                 Log("[Silent Refresh] steamLoginSecure not found on Community. Session invalid.");
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
                    Log("[Auth Stage 2.5] Navigating to dynamicstore/userdata/ to finalize session.");
                    await Task.Delay(5000); 
                    _webView.Source = new Uri("https://store.steampowered.com/dynamicstore/userdata/");  
                    return; 
                }

                // Stage 3: We are on dynamicstore/userdata/, session should be complete
                Log("[Auth Stage 3] Session fully established on dynamicstore/userdata/.");

                var allStoreCookies = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                if (allStoreCookies.Any(c => c.Name == "steamLoginSecure"))
                {
                    Log("[Auth Success] Retrieved steamLoginSecure from Store.");
                    
                    var cookieData = allStoreCookies.Select(c => new 
                    { 
                        c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                    }).ToList();

                    var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });

                    var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                    Directory.CreateDirectory(authTokensPath);
                    await File.WriteAllTextAsync(Path.Combine(authTokensPath, "steam_cookies.json"), json);
                    Log($"Steam cookies saved to: {Path.Combine(authTokensPath, "steam_cookies.json")}");

                    _loginResultTcs?.TrySetResult("SteamLoggedIn");
                    this.Close();
                }
            }
        }
    }
}
