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
    private bool _hasScrapedSuccessfully = false;

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
        this.FormClosing += UbisoftLoginForm_FormClosing;
    }





    private async Task PerformScrapingAndCloseForm()
    {
        // If scraping has already successfully occurred, just complete the task and close the form.
        if (_hasScrapedSuccessfully)
        {
            Console.WriteLine("[DEBUG] PerformScrapingAndCloseForm: Automated scraping previously triggered and completed. Closing form.");
            _tcs.TrySetResult("UbisoftLoggedIn");
            this.Close();
            return;
        }

        Console.WriteLine($"[DEBUG] PerformScrapingAndCloseForm: Started. Initiating new scrape.");
        Console.WriteLine($"[DEBUG] PerformScrapingAndCloseForm: Current URL when started: {_webView.Source.ToString()}");
        
        string? resultStatus = "UbisoftLoggedIn"; // Assume success initially

        try
        {
            // Ensure we are on the correct Ubisoft+ account page before scraping
            string targetUrl = "https://store.ubisoft.com/uk/my-account#account-ubisoftplus";
            if (! _webView.Source.ToString().StartsWith(targetUrl, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[DEBUG] Navigating to target URL: {targetUrl}");
                _webView.Source = new Uri(targetUrl);
                // Wait for navigation to complete
                await Task.Delay(5000); // Give ample time for new page load and redirects
                Console.WriteLine($"[DEBUG] Navigated to: {_webView.Source.ToString()}");
            }

            // Wait for potential page refresh/dynamic content to load after user login
            await Task.Delay(2000); // Additional delay to ensure dynamic content is loaded

            // 1. Capture and Save Cookies
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
            var ubisoftCookiesPath = Path.Combine(authTokensPath, "ubisoft_cookies.json");
            Console.WriteLine($"[DEBUG] Attempting to save Ubisoft cookies to: {ubisoftCookiesPath}");
            await File.WriteAllTextAsync(ubisoftCookiesPath, json);
            Console.WriteLine($"[DEBUG] Ubisoft cookies saved successfully to {ubisoftCookiesPath}");
            
            // 2. Perform Scraping for Status
            string content = await _webView.ExecuteScriptAsync("document.body.textContent");
            content = System.Text.RegularExpressions.Regex.Unescape(content);
            
            Console.WriteLine($"[Debug Scrape] Scrape Final URL: {_webView.Source.ToString()}");
            // For debugging, uncomment to see more content:
            // Console.WriteLine($"[Debug Scrape] Content start: {content.Substring(0, Math.Min(content.Length, 2000))}");

            string status = "None";

            // Look for explicit markers
            if (content.Contains("Ubisoft+ Premium", StringComparison.OrdinalIgnoreCase) && 
                content.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                status = "Ubisoft+ Premium";
            }
            else if (content.Contains("Ubisoft+ Classics", StringComparison.OrdinalIgnoreCase) && 
                     content.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                status = "Ubisoft+ Classics";
            }
            else if (content.Contains("Ubisoft+ PC Access", StringComparison.OrdinalIgnoreCase) && 
                     content.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                status = "Ubisoft+ PC Access";
            }
            else if (content.Contains("Ubisoft+ Premium", StringComparison.OrdinalIgnoreCase) && 
                     content.Contains("Manage subscription", StringComparison.OrdinalIgnoreCase))
            {
                 status = "Ubisoft+ Premium"; // Active implied if "Manage subscription" is present for Premium
            }
            else if (content.Contains("Active", StringComparison.OrdinalIgnoreCase) && 
                     (content.Contains("Ubisoft+", StringComparison.OrdinalIgnoreCase) || content.Contains("Ubisoft Plus", StringComparison.OrdinalIgnoreCase)))
            {
                status = "Ubisoft+ (Unknown Tier)";
            }
            else if (content.Contains("Subscribe now", StringComparison.OrdinalIgnoreCase) || 
                     content.Contains("Join Ubisoft+", StringComparison.OrdinalIgnoreCase) ||
                     content.Contains("Choose your plan", StringComparison.OrdinalIgnoreCase) ||
                     content.Contains("No active subscription", StringComparison.OrdinalIgnoreCase)) // Added explicit negative indicator
            {
                status = "None";
            }

            var ubisoftStatusPath = Path.Combine(authTokensPath, "ubisoft_status.txt");
            Console.WriteLine($"[DEBUG] Attempting to save Ubisoft status to: {ubisoftStatusPath}");
            await File.WriteAllTextAsync(ubisoftStatusPath, status);
            Console.WriteLine($"[DEBUG] Ubisoft status saved successfully to {ubisoftStatusPath}");
            _hasScrapedSuccessfully = true; // Set flag ONLY after successful file writes
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error FinalizeAuth] Exception caught: {ex.Message}");
            resultStatus = null; // Indicate failure
            _tcs.TrySetException(ex); // Set exception to task completion source
        }
        finally
        {
            Console.WriteLine($"[DEBUG] PerformScrapingAndCloseForm: Completed.");
            _tcs.TrySetResult(resultStatus); // Set task result
            this.Close(); // Close the form
        }
    }



    private async void UbisoftLoginForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // Only trigger scraping if the task hasn't been completed yet by other means
        if (_tcs.Task.Status == TaskStatus.WaitingForActivation || _tcs.Task.Status == TaskStatus.Running)
        {
            await PerformScrapingAndCloseForm();
        }
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
            _webView.NavigationCompleted += WebView_NavigationCompleted;

            // Navigate to Ubisoft Store Account Page with Ubisoft+ tab pre-selected
            _webView.Source = new Uri("https://store.ubisoft.com/uk/my-account#account-ubisoftplus");
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

    private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess && !_hasScrapedSuccessfully)
        {
            Console.WriteLine($"[DEBUG] WebView_NavigationCompleted: Navigated successfully to {_webView.Source.ToString()}");
            
            // Give the page a moment to render dynamic content after navigation
            await Task.Delay(2000); 

            // Inject JavaScript to check for login indicators
            string script = @"
                var isLoggedIn = false;
                // Check for elements that typically indicate a logged-in state on Ubisoft's account page
                var accountSection = document.querySelector('section.account-section');
                var welcomeMessage = document.querySelector('.my-account-page__welcome');
                var subscriptionSection = document.getElementById('account-ubisoftplus');

                if ((accountSection && accountSection.innerText.includes('Hello,')) || 
                    (welcomeMessage && welcomeMessage.innerText.includes('Welcome')) ||
                    (subscriptionSection && subscriptionSection.innerText.includes('Ubisoft+'))) {
                    isLoggedIn = true;
                }
                isLoggedIn;
            ";

            string result = await _webView.ExecuteScriptAsync(script);
            bool isLoggedIn = bool.Parse(result);

            if (isLoggedIn)
            {
                Console.WriteLine("[DEBUG] Logged-in state detected, preparing to trigger scraping via PerformScrapingAndCloseForm.");
                await PerformScrapingAndCloseForm();
            }
            else
            {
                Console.WriteLine("[DEBUG] Logged-in state not yet detected or page not ready for scraping.");
            }
        }
        else if (!e.IsSuccess)
        {
            Console.WriteLine($"[Debug] WebView2 Navigation Failed: URL={_webView.Source.ToString()}, Status={e.WebErrorStatus}");
        }
    }


}