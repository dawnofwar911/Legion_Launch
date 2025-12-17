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

            // Navigate to Xbox Cloud Gaming as it requires a valid session
            _webView.Source = new Uri("https://www.xbox.com/en-US/play");
            
            _webView.NavigationCompleted += WebView_NavigationCompleted;
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
            this.Close();
        }
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
            // Check for valid session cookies on xbox.com
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://www.xbox.com");
            
            // "XBXX" or "XBX" are common Xbox authentication/session cookies.
            // Alternatively, detecting we are NOT on a login URL is a good heuristic.
            
            // We'll check for a collection of cookies that imply a session.
            var hasAuthCookie = cookies.Any(c => c.Name.StartsWith("XB") || c.Name.Contains("RPS"));
            var currentUrl = _webView.Source.ToString();

            // If we are on xbox.com and have some Xbox cookies, assume success.
            if (hasAuthCookie && currentUrl.Contains("xbox.com") && !currentUrl.Contains("login.live.com"))
            {
                Console.WriteLine($"[Auth Success] Retrieved Xbox session cookies.");
                
                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                Directory.CreateDirectory(authTokensPath); // Ensure directory exists
                await File.WriteAllTextAsync(Path.Combine(authTokensPath, "xbox_auth_status.txt"), "XboxLoggedIn");
                Console.WriteLine($"Xbox auth status saved to: {Path.Combine(authTokensPath, "xbox_auth_status.txt")}");

                _tcs.TrySetResult("XboxLoggedIn"); // Return a success indicator
                this.Close();
            }
        }
        catch
        {
            // Polling safety
        }
    }
}
