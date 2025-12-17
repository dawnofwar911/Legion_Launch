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

public class UbisoftAuthService : IAuthService
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

                var form = new UbisoftLoginForm(tcs);
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

public class UbisoftLoginForm : Form
{
    private readonly TaskCompletionSource<string?> _tcs;
    private WebView2 _webView;
    private bool _isScraping = false;

    public UbisoftLoginForm(TaskCompletionSource<string?> tcs)
    {
        _tcs = tcs;
        this.Text = "Ubisoft Login - LegionDeck";
        this.Width = 1280;
        this.Height = 900;
        this.StartPosition = FormStartPosition.CenterScreen;

        _webView = new WebView2();
        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += UbisoftLoginForm_Load;
    }

    private async void UbisoftLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            
            await _webView.EnsureCoreWebView2Async(env);

            // Set a consistent User-Agent (Updated to mimic Edge)
            _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0";

            // Handle new window requests
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            _webView.SourceChanged += WebView_SourceChanged;

            // Navigate to Ubisoft Store Account Page - Best entry point
            _webView.Source = new Uri("https://store.ubisoft.com/uk/my-account");
            
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
            if (_isScraping)
            {
                await PerformScrape();
                return;
            }

            var currentUrl = _webView.Source.ToString();
            
            // Console.WriteLine($"[Debug] Checking URL: {currentUrl}");

            bool onAccountPage = currentUrl.Contains("/my-account", StringComparison.OrdinalIgnoreCase) && 
                                !currentUrl.Contains("login", StringComparison.OrdinalIgnoreCase) && 
                                !currentUrl.Contains("signin", StringComparison.OrdinalIgnoreCase);

            if (onAccountPage)
            {
                Console.WriteLine($"[Auth Success] Reached Account Page. Proceeding to scrape subscription status...");
                
                // Capture all cookies from store domain
                var cookieManager = _webView.CoreWebView2.CookieManager;
                var cookies = await cookieManager.GetCookiesAsync("https://store.ubisoft.com");
                var mainCookies = await cookieManager.GetCookiesAsync("https://ubisoft.com");
                var allCookies = cookies.Concat(mainCookies).GroupBy(c => c.Name + c.Domain).Select(g => g.First()).ToList();

                var cookieData = allCookies.Select(c => new 
                { 
                    c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                }).ToList();
                var json = JsonSerializer.Serialize(cookieData, new JsonSerializerOptions { WriteIndented = true });
                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                Directory.CreateDirectory(authTokensPath);
                await File.WriteAllTextAsync(Path.Combine(authTokensPath, "ubisoft_cookies.json"), json);

                // Start Scraping Phase - Navigate to the # tab
                _isScraping = true;
                _webView.Source = new Uri("https://store.ubisoft.com/uk/my-account#account-ubisoftplus");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Error during Ubisoft auth/scrape: {ex.Message}");
        }
    }

    private async Task PerformScrape()
    {
        // Wait for page to be "ready" enough
        try 
        {
            // Wait until document is ready - essential for SPA content
            var maxAttempts = 20; // 20 attempts * 500ms = 10 seconds
            for (int i = 0; i < maxAttempts; i++)
            {
                var readyState = await _webView.ExecuteScriptAsync("document.readyState");
                var unescapedReadyState = JsonSerializer.Deserialize<string>(readyState);
                if (unescapedReadyState == "complete")
                {
                    break;
                }
                await Task.Delay(500); 
            }

            // Give a little more time for dynamic content to render
            await Task.Delay(8000);

            var content = await _webView.ExecuteScriptAsync("document.body.textContent");
            content = System.Text.RegularExpressions.Regex.Unescape(content);
            
            Console.WriteLine($"[Debug Scrape] Scrape Final URL: {_webView.Source.ToString()}");
            Console.WriteLine($"[Debug Scrape] Content start: {content.Substring(0, Math.Min(content.Length, 2000))}");

            string status = "None";
            bool found = false;

            Console.WriteLine($"[Debug Scrape] Contains 'Ubisoft+ Premium' && 'Active': {content.Contains("Ubisoft+ Premium", StringComparison.OrdinalIgnoreCase) && content.Contains("Active", StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"[Debug Scrape] Contains 'Ubisoft+ Classics' && 'Active': {content.Contains("Ubisoft+ Classics", StringComparison.OrdinalIgnoreCase) && content.Contains("Active", StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"[Debug Scrape] Contains 'Ubisoft+ PC Access' && 'Active': {content.Contains("Ubisoft+ PC Access", StringComparison.OrdinalIgnoreCase) && content.Contains("Active", StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"[Debug Scrape] Contains 'Ubisoft+ Premium' && 'Manage subscription': {content.Contains("Ubisoft+ Premium", StringComparison.OrdinalIgnoreCase) && content.Contains("Manage subscription", StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"[Debug Scrape] Contains 'Ubisoft+' && 'Active' (Unknown Tier): {content.Contains("Active", StringComparison.OrdinalIgnoreCase) && (content.Contains("Ubisoft+", StringComparison.OrdinalIgnoreCase) || content.Contains("Ubisoft Plus", StringComparison.OrdinalIgnoreCase))}");
            Console.WriteLine($"[Debug Scrape] Contains 'Subscribe now' (Negative): {content.Contains("Subscribe now", StringComparison.OrdinalIgnoreCase)}");


            // Look for explicit markers (relaxed)
            if (content.Contains("Ubisoft+ Premium", StringComparison.OrdinalIgnoreCase) && 
                content.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                status = "Ubisoft+ Premium";
                found = true;
            }
            else if (content.Contains("Ubisoft+ Classics", StringComparison.OrdinalIgnoreCase) && 
                     content.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                status = "Ubisoft+ Classics";
                found = true;
            }
            else if (content.Contains("Ubisoft+ PC Access", StringComparison.OrdinalIgnoreCase) && 
                     content.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                status = "Ubisoft+ PC Access";
                found = true;
            }
            else if (content.Contains("Ubisoft+ Premium", StringComparison.OrdinalIgnoreCase) && 
                     content.Contains("Manage subscription", StringComparison.OrdinalIgnoreCase))
            {
                 status = "Ubisoft+ Premium"; // Active implied
                 found = true;
            }
            else if (content.Contains("Active", StringComparison.OrdinalIgnoreCase) && 
                     (content.Contains("Ubisoft+", StringComparison.OrdinalIgnoreCase) || content.Contains("Ubisoft Plus", StringComparison.OrdinalIgnoreCase)))
            {
                status = "Ubisoft+ (Unknown Tier)";
                found = true;
            }
            else if (content.Contains("Subscribe now", StringComparison.OrdinalIgnoreCase) || 
                     content.Contains("Join Ubisoft+", StringComparison.OrdinalIgnoreCase) ||
                     content.Contains("Choose your plan", StringComparison.OrdinalIgnoreCase))
            {
                status = "None";
                found = true; // Found negative confirmation
            }

            if (found) 
            {
                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                await File.WriteAllTextAsync(Path.Combine(authTokensPath, "ubisoft_status.txt"), status);
                Console.WriteLine($"[Scrape Success] Ubisoft+ Status: {status}");
                
                _tcs.TrySetResult("UbisoftLoggedIn");
                this.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error Scrape] {ex.Message}");
        }
    }
}