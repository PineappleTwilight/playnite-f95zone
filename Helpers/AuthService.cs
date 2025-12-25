using AngleSharp;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;

namespace F95ZoneMetadataProvider.Helpers
{
    public class AuthService
    {
        private const string BaseUrl = "https://f95zone.to";
        private const string LoginUrl = "https://f95zone.to/login";
        private const string LoginActionUrl = "https://f95zone.to/login/login";

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _handler;

        public AuthService()
        {
            _logger = LogManager.GetLogger();
            _handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true
            };
            _httpClient = new HttpClient(_handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        }

        public async Task<List<Cookie>> Login(string username, string password, string twoFactorCode = null)
        {
            _logger.Info($"Attempting login for user: {username}");

            try
            {
                // 1. Fetch CSRF Token
                var csrfToken = await GetCsrfToken();
                if (string.IsNullOrEmpty(csrfToken))
                {
                    _logger.Error("Failed to retrieve CSRF token.");
                    return null;
                }

                // 2. Prepare Login Data
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("login", username),
                    new KeyValuePair<string, string>("password", password),
                    new KeyValuePair<string, string>("remember", "1"),
                    new KeyValuePair<string, string>("_xfRedirect", "/"),
                    new KeyValuePair<string, string>("_xfToken", csrfToken),
                    new KeyValuePair<string, string>("website_code", "")
                };

                if (!string.IsNullOrEmpty(twoFactorCode))
                {
                    formData.Add(new KeyValuePair<string, string>("2fa", twoFactorCode));
                }

                var content = new FormUrlEncodedContent(formData);

                // 3. Perform Login
                var response = await _httpClient.PostAsync(LoginActionUrl, content);
                
                // 4. Verify Success
                // The ZoneManager checks for xf_user cookie.
                var cookies = _handler.CookieContainer.GetCookies(new Uri(BaseUrl)).Cast<Cookie>().ToList();
                var userCookie = cookies.FirstOrDefault(c => c.Name == "xf_user");

                if (userCookie != null)
                {
                    _logger.Info("Login successful.");
                    return cookies;
                }
                else
                {
                    _logger.Error("Login failed: xf_user cookie not found.");
                    // Log response for debugging if needed, but be careful with credentials
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during login process.");
                return null;
            }
        }

        private async Task<string> GetCsrfToken()
        {
            try
            {
                var response = await _httpClient.GetAsync(LoginUrl);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();
                
                // Use AngleSharp to parse
                var context = BrowsingContext.New(Configuration.Default);
                var document = await context.OpenAsync(req => req.Content(html));

                var tokenInput = document.QuerySelector("input[name='_xfToken']");
                return tokenInput?.GetAttribute("value");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching CSRF token.");
                return null;
            }
        }
    }
}
