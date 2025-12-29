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

namespace LegionDeck.Core.Services;

public class EaAuthService : IAuthService
{
    public void ClearCookies()
    {
        try
        {
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var cookiePath = Path.Combine(authTokensPath, "ea_cookies.json");
            if (File.Exists(cookiePath)) File.Delete(cookiePath);
        }
        catch { }
    }

    public async Task<string?> LoginAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        
        var thread = new Thread(() =>
        {
            var window = new EaLoginForm();
            window.Show();
            
            // Wait for completion signal or window close
            window.Closed += (s, e) => tcs.TrySetResult("WindowClosed");
            
            // We can listen to our own TCS to close the window programmatically if needed
            _ = tcs.Task.ContinueWith(t => 
            {
                if (t.IsCompleted && !window.IsDisposed) 
                    window.Invoke(new Action(window.Close));
            });

            System.Windows.Forms.Application.Run(window);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await tcs.Task;
    }

    public async Task<bool> RefreshTokenAsync()
    {
        Log("[EA Auth] RefreshTokenAsync started.");
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var window = new EaLoginForm(isSilent: true);
                
                var timer = new System.Windows.Forms.Timer { Interval = 30000 }; // 30s timeout
                timer.Tick += (s, e) => { 
                    timer.Stop();
                    Log("[EA Auth] Refresh timed out after 30s.");
                    if (!window.IsDisposed) window.Invoke(new Action(window.Close)); 
                    tcs.TrySetResult(false); 
                };
                timer.Start();

                var watcher = new FileSystemWatcher(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens"), "ea_token.txt");
                watcher.NotifyFilter = NotifyFilters.LastWrite;
                watcher.Changed += (s, e) => 
                { 
                    Log("[EA Auth] FileSystemWatcher detected ea_token.txt update!");
                    if (!window.IsDisposed) window.Invoke(new Action(window.Close)); 
                    tcs.TrySetResult(true); 
                };
                watcher.EnableRaisingEvents = true;

                window.FormClosed += (s, e) => { watcher.Dispose(); timer.Dispose(); };

                Log("[EA Auth] Showing silent login window...");
                System.Windows.Forms.Application.Run(window);
            }
            catch (Exception ex) { 
                Log($"[EA Auth] Exception in Refresh thread: {ex.Message}");
                tcs.TrySetResult(false); 
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var result = await tcs.Task;
        Log($"[EA Auth] RefreshTokenAsync completed with result: {result}");
        return result;
    }
    
    // ... (Keep existing Log and class definitions)


    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\n");
        }
        catch { }
    }
}

public class EaLoginForm : Form
{
    private readonly TaskCompletionSource<string?> _tcs;
    private WebView2 _webView;
    private readonly bool _isSilent;
    private bool _tokenCaptured = false;

    public EaLoginForm(TaskCompletionSource<string?> tcs = null, bool isSilent = false)
    {
        _tcs = tcs ?? new TaskCompletionSource<string?>();
        _isSilent = isSilent;
        
        this.Text = "EA Login - LegionDeck";
        this.Width = 1024;
        this.Height = 768;
        this.StartPosition = FormStartPosition.CenterScreen;

        if (_isSilent)
        {
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0.01; // Low opacity instead of 0
        }

        _webView = new WebView2();
        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += EaLoginForm_Load;
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [EaLoginForm] {message}\n");
        }
        catch { }
    }

    private async void EaLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            Log("EaLoginForm_Load started.");
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            
            await _webView.EnsureCoreWebView2Async(env);
            Log("WebView2 Environment ready.");

            _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

            // Attach the token catcher to REQUESTS
            _webView.CoreWebView2.WebResourceRequested += (s, args) => {
                var uri = args.Request.Uri;
                if (uri.Contains("service-aggregation-layer.juno.ea.com/graphql"))
                {
                    Log($"Intercepted Juno GraphQL Request: {uri}");
                    
                    // Correct way to get headers in WebView2
                    var headers = args.Request.Headers;
                    if (headers.Contains("Authorization"))
                    {
                        var authValue = headers.GetHeader("Authorization");
                        if (!string.IsNullOrEmpty(authValue) && authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = authValue.Substring(7);
                            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                            Directory.CreateDirectory(authTokensPath);
                            File.WriteAllText(Path.Combine(authTokensPath, "ea_token.txt"), token);
                        Log("[SUCCESS] Captured new Juno Bearer Token from Authorization header.");
                        _tokenCaptured = true;
                        
                        // Force close manual login window once we have the token
                        if (!_isSilent)
                        {
                            Log("Manual login successful. Closing window.");
                            _tcs.TrySetResult("EALoggedIn");
                            this.Invoke(new Action(this.Close));
                        }
                        else 
                        {
                            _ = CheckForLogin();
                        }
                        }
                    }
                    else 
                    {
                        // Debug: Log all header names to see if we missed it
                        var headerNames = new List<string>();
                        foreach (var header in headers) { headerNames.Add(header.Key); }
                        Log($"Authorization header missing. Available headers: {string.Join(", ", headerNames)}");
                    }
                }
            };

            // Filters are required for WebResourceRequested
            _webView.CoreWebView2.AddWebResourceRequestedFilter("https://service-aggregation-layer.juno.ea.com/graphql*", CoreWebView2WebResourceContext.All);

            _webView.CoreWebView2.NewWindowRequested += (s, args) => { args.Handled = true; _webView.Source = new Uri(args.Uri); };
            
            Log("Navigating to EA Login flow...");
            _webView.Source = new Uri("https://www.ea.com/login");
            
            _webView.NavigationCompleted += async (s, args) => {
                var currentUrl = _webView.Source.ToString().ToLowerInvariant();
                Log($"Navigation completed to: {currentUrl} (Success: {args.IsSuccess})");
                
                // Broaden check: any ea.com URL that looks like a landing page or home page
                if (currentUrl.Contains("ea.com") && (currentUrl.EndsWith("/") || currentUrl.Contains("/home") || currentUrl.Contains("/en-gb") || currentUrl.Contains("/en-us")))
                {
                    if (!currentUrl.Contains("sales/deals") && !currentUrl.Contains("login"))
                    {
                        Log("Landing page detected. Forcing navigation to Deals to capture token...");
                        _webView.CoreWebView2.Navigate("https://www.ea.com/sales/deals");
                    }
                }
                
                await CheckForLogin();
            };
        }
        catch (Exception ex)
        {
            Log($"[CRITICAL ERROR] WebView initialization failed: {ex.Message}");
            _tcs.TrySetException(ex);
            this.Close();
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _webView.Source = new Uri(e.Uri);
    }

    private async void WebView_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        await CheckForLogin();
    }

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            await CheckForLogin();
        }
    }

    private async Task CheckForLogin()
    {
        try 
        {
            var currentUrl = _webView.Source.ToString().ToLowerInvariant();
            
            // Basic check: if we are redirected back to ea.com and have a 'remid' or 'sid', we are likely logged in.
            if (currentUrl.Contains("ea.com") && !currentUrl.Contains("connect/auth") && !currentUrl.Contains("login"))
            {
                var cookieManager = _webView.CoreWebView2.CookieManager;
                var accountCookies = await cookieManager.GetCookiesAsync("https://accounts.ea.com");
                
                var sid = accountCookies.FirstOrDefault(c => c.Name.Equals("sid", StringComparison.OrdinalIgnoreCase));
                var remid = accountCookies.FirstOrDefault(c => c.Name.Equals("remid", StringComparison.OrdinalIgnoreCase));

                if (sid != null || remid != null)
                {
                    Log($"Cookies found (sid: {sid != null}, remid: {remid != null}). Token captured: {_tokenCaptured}");
                    
                    if (_tokenCaptured)
                    {
                        Log("[Auth Success] Retrieved EA session cookies and Juno token. Closing window.");
                        
                        var cookieData = accountCookies.Select(c => new 
                        { 
                            c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                        }).ToList();

                        var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });
                        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                        Directory.CreateDirectory(authTokensPath);
                        await File.WriteAllTextAsync(Path.Combine(authTokensPath, "ea_cookies.json"), json);
                        
                        _tcs.TrySetResult("EALoggedIn");
                        this.Close();
                    }
                    else 
                    {
                        Log("Cookies present, but waiting for Juno GraphQL call to capture bearer token...");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[Error] Error during EA cookie check: {ex.Message}");
        }
    }
}
