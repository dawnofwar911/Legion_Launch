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

public class EaAuthService : IAuthService
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

                var form = new EaLoginForm(tcs);
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
}

public class EaLoginForm : Form
{
    private readonly TaskCompletionSource<string?> _tcs;
    private WebView2 _webView;

    public EaLoginForm(TaskCompletionSource<string?> tcs)
    {
        _tcs = tcs;
        this.Text = "EA Login - LegionDeck";
        this.Width = 1024;
        this.Height = 768;
        this.StartPosition = FormStartPosition.CenterScreen;

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

            // Handle new window requests
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            _webView.SourceChanged += WebView_SourceChanged;

            // Navigate to EA Login - Let the site handle the redirect and client_id
            _webView.Source = new Uri("https://www.ea.com/login");
            
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
            
            Console.WriteLine($"[Debug] Checking URL: {currentUrl}");

            // Basic check: if we are redirected back to ea.com and have a 'remid' or 'sid', we are likely logged in.
            if (currentUrl.Contains("ea.com", StringComparison.OrdinalIgnoreCase) && !currentUrl.Contains("connect/auth"))
            {
                var accountCookies = await cookieManager.GetCookiesAsync("https://accounts.ea.com");
                var currentUrlCookies = await cookieManager.GetCookiesAsync(currentUrl);
                
                var allCookies = accountCookies.Concat(currentUrlCookies)
                                               .GroupBy(c => c.Name + c.Domain)
                                               .Select(g => g.First())
                                               .ToList();

                if (allCookies.Any())
                {
                     Console.WriteLine("[Debug] Cookies found:");
                     foreach (var c in allCookies)
                     {
                        Console.WriteLine($"  - {c.Name} ({c.Domain})");
                     }
                }

                // 'PLAY_SESSION' is a common indicator of an active EA session on www.ea.com
                var playSession = allCookies.FirstOrDefault(c => c.Name.Equals("PLAY_SESSION", StringComparison.OrdinalIgnoreCase));

                if (playSession != null)
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
                    Console.WriteLine($"EA cookies saved to: {Path.Combine(authTokensPath, "ea_cookies.json")}");

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