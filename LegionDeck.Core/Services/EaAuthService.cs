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
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var window = new EaLoginForm(isSilent: true); // Overload for silent mode
                
                // Set a timeout
                var timer = new System.Windows.Forms.Timer { Interval = 15000 };
                timer.Tick += (s, e) => { 
                    timer.Stop();
                    if (!window.IsDisposed) window.Invoke(new Action(window.Close)); 
                    tcs.TrySetResult(false); 
                };
                timer.Start();

                // Watch for token file update
                var watcher = new FileSystemWatcher(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens"), "ea_token.txt");
                watcher.NotifyFilter = NotifyFilters.LastWrite;
                watcher.Changed += (s, e) => 
                { 
                    if (!window.IsDisposed) window.Invoke(new Action(window.Close)); 
                    tcs.TrySetResult(true); 
                };
                watcher.EnableRaisingEvents = true;

                // Ensure the watcher doesn't block the thread or get GC'd
                window.FormClosed += (s, e) => { watcher.Dispose(); timer.Dispose(); };

                window.Show(); // Ideally minimal/hidden
                System.Windows.Forms.Application.Run(window);
            }
            catch { tcs.TrySetResult(false); }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await tcs.Task;
    }
}

public class EaLoginForm : Form
{
    private readonly TaskCompletionSource<string?> _tcs;
    private WebView2 _webView;
    private readonly bool _isSilent;

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
            this.Opacity = 0;
        }

        _webView = new WebView2();
        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += EaLoginForm_Load;
    }

    private async void EaLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            
            await _webView.EnsureCoreWebView2Async(env);

            // Set a consistent User-Agent
            _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

            // Attach the token catcher immediately
            _webView.CoreWebView2.WebResourceResponseReceived += (s, args) => {
                if (args.Request.Uri.StartsWith("https://service-aggregation-layer.juno.ea.com/graphql"))
                {
                    var headers = args.Request.Headers;
                    if (headers.Contains("Authorization"))
                    {
                        var authHeader = headers.GetHeader("Authorization");
                        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = authHeader.Substring(7);
                            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                            Directory.CreateDirectory(authTokensPath);
                            File.WriteAllText(Path.Combine(authTokensPath, "ea_token.txt"), token);
                            Console.WriteLine("[Auth Success] Nabbed Juno Bearer Token.");
                        }
                    }
                }
            };

            // Handle new window requests
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            _webView.SourceChanged += WebView_SourceChanged;

            // Navigate to EA Deals - This page heavily uses Juno API calls
            _webView.Source = new Uri("https://www.ea.com/sales/deals");
            
            _webView.NavigationCompleted += WebView_NavigationCompleted;
        }
        catch (Exception ex)
        {
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
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var currentUrl = _webView.Source.ToString();
            
            // Basic check: if we are redirected back to ea.com and have a 'remid' or 'sid', we are likely logged in.
            if (currentUrl.Contains("ea.com", StringComparison.OrdinalIgnoreCase) && !currentUrl.Contains("connect/auth"))
            {
                var accountCookies = await cookieManager.GetCookiesAsync("https://accounts.ea.com");
                var currentUrlCookies = await cookieManager.GetCookiesAsync(currentUrl);
                
                var allCookies = accountCookies.Concat(currentUrlCookies)
                                               .GroupBy(c => c.Name + c.Domain)
                                               .Select(g => g.First())
                                               .ToList();

                // 'PLAY_SESSION' or 'sid' are common indicators
                var playSession = allCookies.FirstOrDefault(c => c.Name.Equals("PLAY_SESSION", StringComparison.OrdinalIgnoreCase));
                var sid = allCookies.FirstOrDefault(c => c.Name.Equals("sid", StringComparison.OrdinalIgnoreCase));

                if (playSession != null || sid != null)
                {
                    Console.WriteLine($"[Auth Success] Retrieved EA session cookies.");
                    
                    var cookieData = allCookies.Select(c => new 
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
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Error during EA cookie check: {ex.Message}");
        }
    }
}
