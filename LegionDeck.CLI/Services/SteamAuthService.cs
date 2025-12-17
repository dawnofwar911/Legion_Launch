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

    public static Task<(string pageContent, string finalUrl)> FetchProtectedPageAsync(string url, string cookieJsonPath) // This version takes cookieJsonPath
    {
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
        return tcs.Task;
    }
}

public class SteamLoginForm : Form
{
    private readonly TaskCompletionSource<string?>? _loginResultTcs;
    private readonly TaskCompletionSource<(string pageContent, string finalUrl)>? _fetchResultTcs;
    private readonly string _targetUrl;
    private readonly string? _cookieJsonPath; 
    private WebView2 _webView = new WebView2(); // Initialize here

    // Constructor for Login Mode
    public SteamLoginForm(TaskCompletionSource<string?> loginTcs, string? initialUrl)
    {
        _loginResultTcs = loginTcs;
        _targetUrl = initialUrl ?? "https://steamcommunity.com/login/home/?goto=";
        _cookieJsonPath = null; 
        InitializeComponentCommon();
        this.Text = "Steam Login - LegionDeck";
    }

    // Constructor for Fetch Mode
    public SteamLoginForm(TaskCompletionSource<string?>? loginTcs, string targetUrl, TaskCompletionSource<(string pageContent, string finalUrl)> fetchTcs, string cookieJsonPath) 
    {
        _loginResultTcs = loginTcs; 
        _targetUrl = targetUrl;
        _fetchResultTcs = fetchTcs;
        _cookieJsonPath = cookieJsonPath; 
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
                // Clear old cookies to ensure a clean login, only for Login mode
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
                Console.WriteLine($"[Auth Stage 1] Logged in on Community. Syncing to Store...");

                // Log all cookies before navigating to dynamicstore/userdata/
                var storeCookiesDebug = await cookieManager.GetCookiesAsync("https://store.steampowered.com");
                var communityCookiesDebug = await cookieManager.GetCookiesAsync("https://steamcommunity.com");

                Console.WriteLine("[Debug] Cookies before dynamicstore/userdata/ navigation:");
                foreach (var c in communityCookiesDebug.Concat(storeCookiesDebug))
                {
                    Console.WriteLine($"[Debug]   Cookie: {c.Name}={c.Value} (Domain: {c.Domain}, Path: {c.Path}, Expires: {c.Expires}, Secure: {c.IsSecure}, HttpOnly: {c.IsHttpOnly})");
                }

                _webView.Source = new Uri("https://store.steampowered.com/");
            }
        }
    }
}
