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
using System.Text.RegularExpressions;

namespace LegionDeck.Core.Services;

public class EpicAuthService : IAuthService
{
    private static readonly SemaphoreSlim _webViewSemaphore = new(1, 1);
    private const string AuthEncodedString = "MzRhMDJjZjhmNDQxNGUyOWIxNTkyMTg3NmRhMzZmOWE6ZGFhZmJjY2M3Mzc3NDUwMzlkZmZlNTNkOTRmYzc2Y2Y=";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EpicGamesLauncher";

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [EpicAuthService] {message}\n");
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
                    Log("Starting interactive Epic LoginAsync thread");
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    var form = new EpicLoginForm(tcs, this); 
                    Application.Run(form);
                    Log("Interactive Epic login form closed");
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
        // Not implemented for Epic yet
    }

    public async Task<bool> RefreshSessionAsync()
    {
        Log("Refreshing Epic session...");
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var tokenPath = Path.Combine(authTokensPath, "epic_tokens.json");

        if (!File.Exists(tokenPath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(tokenPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("refresh_token", out var rtProp))
            {
                var refreshToken = rtProp.GetString();
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    return await RenewTokensAsync(refreshToken);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"RefreshSessionAsync failed: {ex.Message}");
        }
        return false;
    }

    public async Task<bool> RenewTokensAsync(string refreshToken)
    {
        Log("Renewing Epic tokens...");
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", AuthEncodedString);
            
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("token_type", "eg1")
            });

            var response = await client.PostAsync("https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token", content);
            var respContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                Directory.CreateDirectory(authTokensPath);
                await File.WriteAllTextAsync(Path.Combine(authTokensPath, "epic_tokens.json"), respContent);
                Log("Epic tokens renewed and saved.");
                return true;
            }
            else
            {
                Log($"Token renewal failed: {response.StatusCode} - {respContent}");
            }
        }
        catch (Exception ex)
        {
            Log($"Renewal failed: {ex.Message}");
        }
        return false;
    }

    public async Task ExchangeCodeForToken(string code)
    {
        Log($"Exchanging code for token: {code}");
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", AuthEncodedString);
            
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("token_type", "eg1")
            });

            var response = await client.PostAsync("https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token", content);
            var respContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                Directory.CreateDirectory(authTokensPath);
                await File.WriteAllTextAsync(Path.Combine(authTokensPath, "epic_tokens.json"), respContent);
                Log("Epic tokens saved successfully.");
            }
            else
            {
                Log($"Token exchange failed: {response.StatusCode} - {respContent}");
                throw new Exception($"Token exchange failed: {respContent}");
            }
        }
        catch (Exception ex)
        {
            Log($"Exchange failed: {ex.Message}");
            throw;
        }
    }

    public async Task<string?> ExchangeSidForExchangeCode(string sid)
    {
        Log($"Exchanging SID for Exchange Code...");
        try
        {
            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler { CookieContainer = cookieContainer, UseCookies = true };
            using var client = new HttpClient(handler);

            client.DefaultRequestHeaders.Add("X-Epic-Event-Action", "login");
            client.DefaultRequestHeaders.Add("X-Epic-Event-Category", "login");
            client.DefaultRequestHeaders.Add("X-Epic-Strategy-Flags", "");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

            // 1. Set SID
            var sidResp = await client.GetAsync($"https://www.epicgames.com/id/api/set-sid?sid={sid}");
            if (!sidResp.IsSuccessStatusCode) Log($"[Warning] Set-SID returned {sidResp.StatusCode}");

            // 2. Get CSRF
            var csrfResp = await client.GetAsync("https://www.epicgames.com/id/api/csrf");
            
            // Extract XSRF-TOKEN reliably from CookieContainer
            var cookies = cookieContainer.GetCookies(new Uri("https://www.epicgames.com/id/api/csrf"));
            string? xsrfToken = null;
            
            foreach (Cookie c in cookies)
            {
                if (c.Name == "XSRF-TOKEN")
                {
                    xsrfToken = c.Value;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(xsrfToken))
            {
                client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", xsrfToken);
                Log($"XSRF-TOKEN extracted: {xsrfToken}");
            }
            else
            {
                Log("[Error] XSRF-TOKEN cookie not found!");
            }
            
            var country = "US";
            try
            {
                country = System.Globalization.CultureInfo.CurrentCulture.Name.Split('-').Last().ToUpper();
            }
            catch { }
            
            cookieContainer.Add(new Uri("https://www.epicgames.com"), new Cookie("EPIC_COUNTRY", country)); 

            // 3. Generate Exchange Code
            // Try standard endpoint first
            var exchangeUrl = "https://www.epicgames.com/id/api/exchange/generate";
            var response = await client.PostAsync(exchangeUrl, null);
            
            if (!response.IsSuccessStatusCode)
            {
                Log($"[Warning] Exchange generate failed ({response.StatusCode}). Trying fallback...");
                exchangeUrl = "https://www.epicgames.com/id/api/exchange";
                response = await client.PostAsync(exchangeUrl, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            Log($"Exchange response: {json}");
            
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("code", out var codeElement))
            {
                return codeElement.GetString();
            }
        }
        catch (Exception ex)
        {
            Log($"SID Exchange failed: {ex.Message}");
        }
        return null;
    }
}

public class EpicLoginForm : Form
{
    private readonly TaskCompletionSource<string?> _loginResultTcs;
    private readonly EpicAuthService _authService;
    private WebView2 _webView = new WebView2(); 
    private const string RedirectUrl = "https://www.epicgames.com/id/api/redirect";

    public EpicLoginForm(TaskCompletionSource<string?> loginTcs, EpicAuthService authService)
    {
        _loginResultTcs = loginTcs;
        _authService = authService;
        InitializeComponent();
        this.Text = "Epic Games Login - LegionDeck";
    }

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [EpicLoginForm] {message}\n");
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

        this.Load += EpicLoginForm_Load;
        this.FormClosed += EpicLoginForm_FormClosed;
    }

    private void EpicLoginForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _webView?.Dispose();
    }

    private async void EpicLoginForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2_Epic");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            _webView.Source = new Uri($"https://www.epicgames.com/id/login?redirectUrl={Uri.EscapeDataString(RedirectUrl)}");
        }
        catch (Exception ex)
        {
            Log($"Load failed: {ex.Message}");
            _loginResultTcs.TrySetException(ex);
            this.Close();
        }
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;

        var url = _webView.Source.ToString();

        // 1. Initial Redirect Check
        if (url.StartsWith(RedirectUrl))
        {
            Log($"Redirect URL loaded: {url}");
            try
            {
                // Try to get the body content. Use script that handles both raw text and JSON-in-pre scenarios.
                var script = "(document.getElementsByTagName('pre')[0] || document.body).innerText";
                var json = await _webView.ExecuteScriptAsync(script);
                var bodyText = System.Text.Json.JsonSerializer.Deserialize<string>(json);
                
                Log($"Body content (first 100 chars): {(bodyText?.Length > 100 ? bodyText.Substring(0, 100) : bodyText)}");

                if (!string.IsNullOrEmpty(bodyText))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(bodyText);
                    
                    // 1a. Check for direct code (Auth Code Flow / Magic Link)
                    if (doc.RootElement.TryGetProperty("code", out var codeProp) || 
                        doc.RootElement.TryGetProperty("authorizationCode", out codeProp))
                    {
                        var code = codeProp.GetString();
                        if (!string.IsNullOrEmpty(code))
                        {
                            Log($"Authorization Code found! {code.Substring(0, 5)}...");
                            await _authService.ExchangeCodeForToken(code);
                            _loginResultTcs.TrySetResult("EpicCode:" + code);
                            this.Close();
                            return;
                        }
                    }

                    // 1b. Check for SID (Web Login Flow) -> Need to fetch Code via Auth Code URL
                    // Only do this if we aren't ALREADY on the magic link (to prevent loops)
                    if (!url.Contains("clientId=") && doc.RootElement.TryGetProperty("sid", out var sidProp))
                    {
                        var sid = sidProp.GetString();
                        if (!string.IsNullOrEmpty(sid))
                        {
                            Log($"Session ID (SID) found: {sid.Substring(0, 5)}... Navigating to Auth Code URL...");
                            
                            var clientId = "34a02cf8f4414e29b15921876da36f9a"; 
                            var authCodeUrl = $"https://www.epicgames.com/id/api/redirect?clientId={clientId}&responseType=code";
                            
                            _webView.Source = new Uri(authCodeUrl);
                            return;
                        }
                    }
                }
                Log("Redirect loaded but no valid Code or SID found in body.");
            }
            catch (Exception ex) { Log($"Error processing redirect: {ex.Message}"); }
        }
    }
}