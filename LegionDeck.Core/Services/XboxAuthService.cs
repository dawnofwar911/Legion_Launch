using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using System.Collections.Generic;

namespace LegionDeck.Core.Services;

public class XboxAuthService : IAuthService
{
    private const string client_id = "38cd2fa8-66fd-4760-afb2-405eb65d5b0c"; 
    private const string redirect_uri = "https://login.live.com/oauth20_desktop.srf";
    private const string scope = "Xboxlive.signin Xboxlive.offline_access";

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [XboxAuthService] {message}\n");
        }
        catch { }
    }

    public void ClearCookies()
    {
        try
        {
            var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
            var tokenPath = Path.Combine(authTokensPath, "xbox_live_tokens.json");
            if (File.Exists(tokenPath)) File.Delete(tokenPath);
            Log("Cleared Xbox tokens.");
        }
        catch (Exception ex) { Log($"Error clearing tokens: {ex.Message}"); }
    }

    public async Task<string?> RefreshAccessTokenAsync()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var tokenPath = Path.Combine(authTokensPath, "xbox_live_tokens.json");
        if (!File.Exists(tokenPath)) return null;

        try
        {
            Log("Refreshing Xbox Access Token...");
            var json = File.ReadAllText(tokenPath);
            using var doc = JsonDocument.Parse(json);
            
            string? refreshToken = null;
            if (doc.RootElement.TryGetProperty("refresh_token", out var prop)) refreshToken = prop.GetString();
            else if (doc.RootElement.TryGetProperty("RefreshToken", out prop)) refreshToken = prop.GetString();

            if (string.IsNullOrEmpty(refreshToken)) return null;

            using var client = new HttpClient();
            var postData = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "scope", scope },
                { "client_id", client_id },
                { "redirect_uri", redirect_uri }
            };

            var response = await client.PostAsync("https://login.live.com/oauth20_token.srf", new FormUrlEncodedContent(postData));
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var tokenData = JsonDocument.Parse(responseJson);
                var newAccessToken = tokenData.RootElement.GetProperty("access_token").GetString();
                var newRefreshToken = tokenData.RootElement.GetProperty("refresh_token").GetString();

                var newData = new Dictionary<string, object>
                {
                    { "access_token", newAccessToken! },
                    { "refresh_token", newRefreshToken! },
                    { "expires_in", tokenData.RootElement.GetProperty("expires_in").GetInt32() },
                    { "creation_date", DateTime.Now }
                };

                File.WriteAllText(tokenPath, JsonSerializer.Serialize(newData, new JsonSerializerOptions { WriteIndented = true }));
                Log("Token refreshed successfully.");
                return newAccessToken;
            }
        }
        catch (Exception ex) { Log($"Refresh Error: {ex.Message}"); }
        return null;
    }

    public async Task<(string? AuthHeader, string? Xuid)> GetXstsTokenAsync()
    {
        var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
        var tokenPath = Path.Combine(authTokensPath, "xbox_live_tokens.json");
        if (!File.Exists(tokenPath)) return (null, null);

        try
        {
            var json = File.ReadAllText(tokenPath);
            using var doc = JsonDocument.Parse(json);
            
            string? accessToken = null;
            if (doc.RootElement.TryGetProperty("access_token", out var prop)) accessToken = prop.GetString();
            else if (doc.RootElement.TryGetProperty("AccessToken", out prop)) accessToken = prop.GetString();

            if (string.IsNullOrEmpty(accessToken)) return (null, null);

            var result = await PerformHandshake(accessToken);
            if (result.AuthHeader == null)
            {
                Log("Initial handshake failed. Attempting token refresh...");
                var freshToken = await RefreshAccessTokenAsync();
                if (!string.IsNullOrEmpty(freshToken)) result = await PerformHandshake(freshToken);
            }
            return result;
        }
        catch (Exception ex) { Log($"GetXstsTokenAsync Exception: {ex.Message}"); return (null, null); }
    }

    private async Task<(string? AuthHeader, string? Xuid)> PerformHandshake(string accessToken)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.Add("x-xbl-contract-version", "1");

            // 1. User Authenticate
            Log("PerformHandshake: Step 1 (User Authenticate)...");
            var authRequest = new Dictionary<string, object>
            {
                { "RelyingParty", "http://auth.xboxlive.com" },
                { "TokenType", "JWT" },
                { "Properties", new Dictionary<string, string> { { "AuthMethod", "RPS" }, { "SiteName", "user.auth.xboxlive.com" }, { "RpsTicket", $"d={accessToken}" } } }
            };

            var authResponse = await client.PostAsync("https://user.auth.xboxlive.com/user/authenticate", 
                new StringContent(JsonSerializer.Serialize(authRequest), Encoding.UTF8, "application/json"));
            
            var authJson = await authResponse.Content.ReadAsStringAsync();
            if (!authResponse.IsSuccessStatusCode) { Log($"Step 1 Failed ({authResponse.StatusCode}): {authJson}"); return (null, null); }

            using var authDoc = JsonDocument.Parse(authJson);
            var userToken = authDoc.RootElement.GetProperty("Token").GetString();
            var xui = authDoc.RootElement.GetProperty("DisplayClaims").GetProperty("xui").EnumerateArray().First();
            var uhs = xui.GetProperty("uhs").GetString();

            // 2. XSTS Authorize
            Log("PerformHandshake: Step 2 (XSTS Authorize)...");
            var xstsRequest = new Dictionary<string, object>
            {
                { "RelyingParty", "http://xboxlive.com" },
                { "TokenType", "JWT" },
                { "Properties", new Dictionary<string, object> { { "UserTokens", new[] { userToken } }, { "SandboxId", "RETAIL" } } }
            };

            var xstsResponse = await client.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", 
                new StringContent(JsonSerializer.Serialize(xstsRequest), Encoding.UTF8, "application/json"));
            
            var xstsJson = await xstsResponse.Content.ReadAsStringAsync();
            if (!xstsResponse.IsSuccessStatusCode) { Log($"Step 2 Failed ({xstsResponse.StatusCode}): {xstsJson}"); return (null, null); }

            using var xstsDoc = JsonDocument.Parse(xstsJson);
            var xstsToken = xstsDoc.RootElement.GetProperty("Token").GetString();
            var xstsXui = xstsDoc.RootElement.GetProperty("DisplayClaims").GetProperty("xui").EnumerateArray().First();
            var xuid = xstsXui.GetProperty("xid").GetString();

            Log("PerformHandshake: SUCCESS.");
            return ($"XBL3.0 x={uhs};{xstsToken}", xuid);
        }
        catch (Exception ex) { Log($"PerformHandshake Exception: {ex.Message}"); return (null, null); }
    }

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
            catch (Exception ex) { Log($"Xbox login thread error: {ex.Message}"); tcs.TrySetException(ex); }
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
    private const string client_id = "38cd2fa8-66fd-4760-afb2-405eb65d5b0c";
    private const string redirect_uri = "https://login.live.com/oauth20_desktop.srf";
    private const string scope = "Xboxlive.signin Xboxlive.offline_access";

    private void Log(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "startup.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [XboxLoginForm] {message}\n");
        }
        catch { }
    }

    public XboxLoginForm(TaskCompletionSource<string?> tcs)
    {
        _tcs = tcs;
        this.Text = "Xbox Login - LegionDeck";
        this.Width = 600;
        this.Height = 700;
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
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "WebView2_Xbox");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
            _webView.CoreWebView2.CookieManager.DeleteAllCookies();

            _webView.SourceChanged += async (s, args) =>
            {
                var url = _webView.Source.ToString();
                if (url.Contains("code="))
                {
                    var uri = new Uri(url);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var code = query["code"];
                    if (!string.IsNullOrEmpty(code)) await ExchangeCodeForToken(code);
                }
            };

            var loginUrl = $"https://login.live.com/oauth20_authorize.srf?client_id={client_id}&response_type=code&approval_prompt=auto&scope={Uri.EscapeDataString(scope)}&redirect_uri={Uri.EscapeDataString(redirect_uri)}";
            _webView.Source = new Uri(loginUrl);
        }
        catch (Exception ex) { Log($"Load Exception: {ex.Message}"); _tcs.TrySetException(ex); this.Close(); }
    }

    private async Task ExchangeCodeForToken(string code)
    {
        try 
        {
            using var client = new HttpClient();
            var postData = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "scope", scope },
                { "client_id", client_id },
                { "redirect_uri", redirect_uri }
            };

            var response = await client.PostAsync("https://login.live.com/oauth20_token.srf", new FormUrlEncodedContent(postData));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var tokenData = new Dictionary<string, object>
                {
                    { "access_token", doc.RootElement.GetProperty("access_token").GetString()! },
                    { "refresh_token", doc.RootElement.GetProperty("refresh_token").GetString()! },
                    { "expires_in", doc.RootElement.GetProperty("expires_in").GetInt32() },
                    { "creation_date", DateTime.Now }
                };

                var authTokensPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionDeck", "AuthTokens");
                Directory.CreateDirectory(authTokensPath);
                File.WriteAllText(Path.Combine(authTokensPath, "xbox_live_tokens.json"), JsonSerializer.Serialize(tokenData, new JsonSerializerOptions { WriteIndented = true }));
                Log("Tokens saved successfully.");
                _tcs.TrySetResult("XboxLoggedIn");
                this.Close();
            }
        }
        catch (Exception ex) { Log($"Exchange Exception: {ex.Message}"); _tcs.TrySetException(ex); this.Close(); }
    }
}
