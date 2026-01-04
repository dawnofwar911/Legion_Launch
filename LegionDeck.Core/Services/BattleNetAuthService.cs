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

namespace LegionDeck.Core.Services;

public class BattleNetAuthService : IAuthService
{
    private static readonly SemaphoreSlim _webViewSemaphore = new(1, 1);
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    public static string BaseUrl => System.Globalization.CultureInfo.CurrentCulture.Name.EndsWith("GB") || 
                                     System.Globalization.CultureInfo.CurrentCulture.Name.Contains("-EU") 
                                     ? "https://eu.account.battle.net" : "https://account.battle.net";

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [BattleNetAuthService] {message}\n");
        }
        catch {{ }}
    }

    public async Task<string?> LoginAsync()
    {
        await _webViewSemaphore.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<string?>();

            var thread = new Thread(() =>
            {
                try
                {
                    Log("Starting interactive Battle.net LoginAsync thread (direct to refresh endpoint)");
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    var form = new BattleNetLoginForm(tcs, BaseUrl + "/oauth2/authorization/account-settings"); 
                    Application.Run(form);
                    Log("Interactive Battle.net login form closed");
                }
                catch (Exception ex)
                {
                    Log($"LoginAsync thread exception: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return await tcs.Task;
        }
        finally
        {
            _webViewSemaphore.Release();
        }
    }

    public void ClearCookies()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens", "battlenet_cookies.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch {{ }}
    }

    public async Task<bool> RefreshSessionAsync()
    {
        await _webViewSemaphore.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<bool>();

            var thread = new Thread(() =>
            {
                try
                {
                    Log("Starting Battle.net RefreshSessionAsync thread");
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    var form = new BattleNetRefreshForm(tcs, BaseUrl + "/oauth2/authorization/account-settings"); 
                    Application.Run(form);
                    Log("Battle.net refresh form closed");
                }
                catch (Exception ex)
                {
                    Log($"RefreshSessionAsync thread exception: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return await tcs.Task;
        }
        finally
        {
            _webViewSemaphore.Release();
        }
    }

    public static async Task<(string pageContent, string finalUrl)> FetchProtectedPageAsync(string url)
    {
        await _webViewSemaphore.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<(string, string)>();
            var thread = new Thread(() =>
            {
                try
                {
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    
                    var form = new BattleNetLoginForm(tcs, url); 
                    Application.Run(form);
                }
                catch (Exception ex) {{ tcs.TrySetException(ex); }}
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return await tcs.Task;
        }
        finally
        {
            _webViewSemaphore.Release();
        }
    }
}

public class BattleNetLoginForm : Form
{
    private readonly TaskCompletionSource<string?>? _loginResultTcs;
    private readonly TaskCompletionSource<(string pageContent, string finalUrl)>? _fetchResultTcs;
    private readonly string _targetUrl;
    private bool _isRefreshing = false;
    private WebView2 _webView = new WebView2(); 
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public BattleNetLoginForm(TaskCompletionSource<string?> loginTcs, string targetUrl)
    {
        _loginResultTcs = loginTcs;
        _targetUrl = targetUrl; // This will be the OAuth account-settings URL directly
        InitializeComponent();
        this.Text = "Battle.net Login - LegionDeck";
    }

    public BattleNetLoginForm(TaskCompletionSource<(string, string)> fetchTcs, string targetUrl)
    {
        _fetchResultTcs = fetchTcs;
        _targetUrl = targetUrl;
        _isRefreshing = true;
        InitializeComponent();
        this.Text = "LegionDeck - Fetching Battle.net Data...";
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [BattleNetLoginForm] {message}\n");
        }
        catch {{ }}
    }

    private void InitializeComponent()
    {
        this.Width = 1024;
        this.Height = 768;
        this.StartPosition = FormStartPosition.CenterScreen;

        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += BattleNetLoginForm_Load;
        this.FormClosed += (s, e) => _webView?.Dispose();
    }

    private async void BattleNetLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2_BNet");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.UserAgent = UserAgent;
            
            await InjectCookiesAsync();

            _webView.NavigationCompleted += WebView_NavigationCompleted;
            
            if (_isRefreshing)
            {
                Log("Navigating to account-settings to refresh session...");
                _webView.Source = new Uri(BattleNetAuthService.BaseUrl + "/oauth2/authorization/account-settings");
            }
            else
            {
                _webView.Source = new Uri(_targetUrl);
            }
        }
        catch (Exception ex)
        {
            Log($"Load failed: {ex.Message}");
            _loginResultTcs?.TrySetException(ex);
            _fetchResultTcs?.TrySetException(ex);
            this.Close();
        }
    }

    private async Task InjectCookiesAsync()
    {
        try
        {
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var cookiePath = Path.Combine(authTokensPath, "battlenet_cookies.json");
            
            if (File.Exists(cookiePath))
            {
                Log("Injecting saved Battle.net cookies...");
                var json = await File.ReadAllTextAsync(cookiePath);
                var cookies = JsonSerializer.Deserialize<List<SerializableCookie>>(json);
                if (cookies != null)
                {
                    var cookieManager = _webView.CoreWebView2.CookieManager;
                    foreach (var c in cookies)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.Value) && !string.IsNullOrEmpty(c.Domain))
                            {
                                var cookie = cookieManager.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                                cookieManager.AddOrUpdateCookie(cookie);
                            }
                        }
                        catch {{ }}
                    }
                }
            }
        }
        catch (Exception ex) {{ Log($"Cookie injection failed: {ex.Message}"); }}
    }

    private async Task SaveCookiesAsync()
    {
        try
        {
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var allCookies = await cookieManager.GetCookiesAsync(null); 

            var battlenetCookies = allCookies
                .Where(c => c.Domain.Contains("battle.net", StringComparison.OrdinalIgnoreCase))
                .Select(c => new SerializableCookie { 
                    Name = c.Name, 
                    Value = c.Value, 
                    Domain = c.Domain, 
                    Path = c.Path
                })
                .ToList();
            
            var json = JsonSerializer.Serialize(battlenetCookies, new JsonSerializerOptions { WriteIndented = true });
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            Directory.CreateDirectory(authTokensPath);
            await File.WriteAllTextAsync(Path.Combine(authTokensPath, "battlenet_cookies.json"), json);
            Log($"Battle.net cookies saved for {battlenetCookies.Count} entries.");
        }
        catch (Exception ex) {{ Log($"Cookie saving failed: {ex.Message}"); }}
    }

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var url = _webView.Source.ToString();
        var uri = new Uri(url);
        Log($"Navigation completed: {url}");

        if (_loginResultTcs != null)
        {
            if (uri.Host.Contains("account.battle.net") && !url.Contains("/login/", StringComparison.OrdinalIgnoreCase))
            {
                Log($"Login detected: Success on authenticated page {url}.");
                await SaveCookiesAsync();
                _loginResultTcs.TrySetResult("BattleNetLoggedIn");
                this.Close();
            }
            else if (url.Contains("/login/", StringComparison.OrdinalIgnoreCase))
            {
                Log($"Login page detected. Waiting for user interaction: {url}");
            }
            else
            {
                Log($"Login form: Unhandled navigation during login flow: {url}. Waiting for success or login page.");
            }
        }
        else if (_fetchResultTcs != null)
        {
            if (url.Contains("/login/"))
            {
                 Log("Redirected to Login. Session expired.");
                 _fetchResultTcs.TrySetResult((string.Empty, url));
                 this.Close();
                 return;
            }

            if (_isRefreshing && (url.Contains("/overview") || url.Contains("/account-settings"))) 
            {
                Log($"Session refreshed. Navigating to target API: {_targetUrl}");
                _isRefreshing = false;
                await Task.Delay(2000); 
                _webView.Source = new Uri(_targetUrl);
                return;
            }

            if (!_isRefreshing && (url.Contains("/api/") || url.Contains("/games-and-subs") || url.Contains("/classic-games"))) 
            {
                try 
                {
                    Log("Reading API content from body...");
                    await Task.Delay(3000); 
                    var content = await _webView.ExecuteScriptAsync("document.body.innerText");
                    var unescaped = JsonSerializer.Deserialize<string>(content);
                    _fetchResultTcs.TrySetResult((unescaped ?? string.Empty, url));
                }
                catch (Exception ex)
                {
                    _fetchResultTcs.TrySetException(ex);
                }
                finally
                {
                    this.Close();
                }
            }
        }
    }
}

public class BattleNetRefreshForm : Form
{
    private readonly TaskCompletionSource<bool> _refreshResultTcs;
    private readonly string _targetUrl;
    private WebView2 _webView = new WebView2(); 
    private System.Windows.Forms.Timer _closeTimer;

    public BattleNetRefreshForm(TaskCompletionSource<bool> refreshTcs, string targetUrl)
    {
        _refreshResultTcs = refreshTcs;
        _targetUrl = targetUrl;
        InitializeComponent();
        this.Text = "Battle.net Refresh Session - LegionDeck";
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;

        _closeTimer = new System.Windows.Forms.Timer();
        _closeTimer.Interval = 10000; 
        _closeTimer.Tick += CloseTimer_Tick;
        _closeTimer.Start();
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [BattleNetRefreshForm] {message}\n");
        }
        catch {{ }}
    }

    private void InitializeComponent()
    {
        this.Width = 1024;
        this.Height = 768;
        this.StartPosition = FormStartPosition.CenterScreen;

        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += BattleNetRefreshForm_Load;
        this.FormClosed += (s, e) => {
            _webView?.Dispose();
            _closeTimer.Stop();
        };
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        Log("Refresh form: Timeout reached. Forcing close as failed.");
        _closeTimer.Stop();
        _refreshResultTcs.TrySetResult(false);
        this.Close();
    }

    private async void BattleNetRefreshForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2_BNet");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.UserAgent = BattleNetAuthService.UserAgent;
            
            await InjectCookiesAsync();

            _webView.NavigationCompleted += WebView_NavigationCompleted;
            _webView.Source = new Uri(_targetUrl);
        }
        catch (Exception ex)
        {
            Log($"Load failed: {ex.Message}");
            _refreshResultTcs?.TrySetException(ex);
            this.Close();
        }
    }

    private async Task InjectCookiesAsync()
    {
        try
        {
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var cookiePath = Path.Combine(authTokensPath, "battlenet_cookies.json");
            
            if (File.Exists(cookiePath))
            {
                Log("Injecting saved Battle.net cookies for refresh...");
                var json = await File.ReadAllTextAsync(cookiePath);
                var cookies = JsonSerializer.Deserialize<List<SerializableCookie>>(json);
                if (cookies != null)
                {
                    var cookieManager = _webView.CoreWebView2.CookieManager;
                    foreach (var c in cookies)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.Value) && !string.IsNullOrEmpty(c.Domain))
                            {
                                var cookie = cookieManager.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                                cookieManager.AddOrUpdateCookie(cookie);
                            }
                        }
                        catch {{ }}
                    }
                }
            }
        }
        catch (Exception ex) {{ Log($"Cookie injection failed during refresh: {ex.Message}"); }}
    }

    private async Task SaveCookiesAsync()
    {
        try
        {
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var allCookies = await cookieManager.GetCookiesAsync(null); 

            var battlenetCookies = allCookies
                .Where(c => c.Domain.Contains("battle.net", StringComparison.OrdinalIgnoreCase))
                .Select(c => new SerializableCookie { 
                    Name = c.Name, 
                    Value = c.Value, 
                    Domain = c.Domain, 
                    Path = c.Path
                })
                .ToList();
            
            var json = JsonSerializer.Serialize(battlenetCookies, new JsonSerializerOptions { WriteIndented = true });
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            Directory.CreateDirectory(authTokensPath);
            await File.WriteAllTextAsync(Path.Combine(authTokensPath, "battlenet_cookies.json"), json);
            Log($"Battle.net cookies saved during refresh. Count: {battlenetCookies.Count}");
        }
        catch (Exception ex) {{ Log($"Cookie saving failed during refresh: {ex.Message}"); }}
    }


    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var url = _webView.Source.ToString();
        Log($"Navigation completed during refresh: {url}");

        if ((url.Contains("/oauth2/authorization/account-settings") || url.Contains("/overview")) && !url.Contains("/login/"))
        {
            Log("Session refresh successful.");
            await SaveCookiesAsync();
            _refreshResultTcs.TrySetResult(true);
            this.Close();
        }
        else if (url.Contains("/login/"))
        {
            Log("Session refresh failed: Redirected to login page.");
            _refreshResultTcs.TrySetResult(false);
            this.Close();
        }
        else
        {
            Log($"Refresh form: Unexpected navigation during refresh: {url}. Assuming success and closing.");
            await SaveCookiesAsync(); 
            _refreshResultTcs.TrySetResult(true);
            this.Close();
        }
    }
}

public class SerializableCookie
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Domain { get; set; }
    public string? Path { get; set; }
}
