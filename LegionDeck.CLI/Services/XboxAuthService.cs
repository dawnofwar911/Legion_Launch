using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LegionDeck.CLI.Services;

public class XboxAuthService : IAuthService
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

                var form = new XboxLoginForm(tcs);
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

public class XboxLoginForm : Form
{
    private readonly TaskCompletionSource<string?> _tcs;
    private WebView2 _webView;

    public XboxLoginForm(TaskCompletionSource<string?> tcs)
    {
        _tcs = tcs;
        this.Text = "Xbox Login - LegionDeck";
        this.Width = 1024;
        this.Height = 768;
        this.StartPosition = FormStartPosition.CenterScreen;

        _webView = new WebView2();
        _webView.Dock = DockStyle.Fill;
        this.Controls.Add(_webView);

        this.Load += XboxLoginForm_Load;
    }

    private async void XboxLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            
            await _webView.EnsureCoreWebView2Async(env);

            // Set a consistent User-Agent
            _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

            // Handle new window requests (popups) by navigating the current WebView instead
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            _webView.SourceChanged += WebView_SourceChanged;

            // Navigate to Xbox Account page to trigger login if not authenticated
            _webView.Source = new Uri("https://account.xbox.com/");
            
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
        e.Handled = true; // Prevent the new window from opening
        _webView.Source = new Uri(e.Uri); // Navigate the current window to the requested URL
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

            // Attempt to retrieve all cookies from relevant domains
            var xboxCookies = await cookieManager.GetCookiesAsync("https://www.xbox.com");
            var liveCookies = await cookieManager.GetCookiesAsync("https://login.live.com");
            var microsoftCookies = await cookieManager.GetCookiesAsync("https://account.microsoft.com");
            
            var allCookies = xboxCookies.Concat(liveCookies).Concat(microsoftCookies).ToList();

            if (allCookies.Any())
            {
                 Console.WriteLine("[Debug] Cookies found:");
                 foreach (var c in allCookies)
                 {
                    Console.WriteLine($"  - {c.Name} ({c.Domain})");
                 }
            }

            // Check if the current URL indicates a successful Xbox login landing page
            // ... (rest of logic)
            bool isOnXboxDomain = currentUrl.Contains("xbox.com", StringComparison.OrdinalIgnoreCase) ||
                                  currentUrl.Contains("microsoft.com", StringComparison.OrdinalIgnoreCase);
            bool isNotLoginPage = !currentUrl.Contains("login.live.com", StringComparison.OrdinalIgnoreCase) &&
                                  !currentUrl.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase);

            if (isOnXboxDomain && isNotLoginPage)
            {
                // Combine and filter for relevant authentication cookies using the cookies already fetched
                var allRelevantCookies = xboxCookies.Concat(liveCookies).Concat(microsoftCookies)
                                                    .Where(c => c.Name.StartsWith("XBL", StringComparison.OrdinalIgnoreCase) || 
                                                                c.Name.StartsWith("XboxLive", StringComparison.OrdinalIgnoreCase) || 
                                                                c.Name.StartsWith("XBXX", StringComparison.OrdinalIgnoreCase) || // Auth tokens often start with XBXX
                                                                c.Name.Contains("x-xbl-contract-version", StringComparison.OrdinalIgnoreCase) || 
                                                                c.Name.Contains("t=", StringComparison.OrdinalIgnoreCase) || 
                                                                c.Name.Contains("MSFMAPPS_CS", StringComparison.OrdinalIgnoreCase) ||
                                                                c.Name.Contains("MSPShared", StringComparison.OrdinalIgnoreCase) ||
                                                                c.Name.Contains("Csp", StringComparison.OrdinalIgnoreCase) || 
                                                                c.Name.Contains("RPSSecAuth", StringComparison.OrdinalIgnoreCase)
                                                                )
                                                    .GroupBy(c => c.Name + c.Domain) // Deduplicate
                                                    .Select(g => g.First())
                                                    .ToList();

                // Check if we have enough indicators for a successful login
                if (allRelevantCookies.Any(c => c.Name.StartsWith("XBL", StringComparison.OrdinalIgnoreCase) || 
                                                c.Name.StartsWith("XBXX", StringComparison.OrdinalIgnoreCase) ||
                                                c.Name.Contains("x-xbl-contract-version", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[Auth Success] Retrieved Xbox session cookies.");
                    
                    var cookieData = allRelevantCookies.Select(c => new 
                    { 
                        c.Name, c.Value, c.Domain, c.Path, c.Expires, c.IsSecure, c.IsHttpOnly 
                    }).ToList();

                    var json = System.Text.Json.JsonSerializer.Serialize(cookieData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                    var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                    Directory.CreateDirectory(authTokensPath); // Ensure directory exists
                    await File.WriteAllTextAsync(Path.Combine(authTokensPath, "xbox_cookies.json"), json);
                    Console.WriteLine($"Xbox cookies saved to: {Path.Combine(authTokensPath, "xbox_cookies.json")}");

                    _tcs.TrySetResult("XboxLoggedIn"); // Indicate success
                    this.Close();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Error during Xbox cookie check: {ex.Message}");
            // Don't set TCS to exception here, let polling continue or timeout.
        }
    }
}
