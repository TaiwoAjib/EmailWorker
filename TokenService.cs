using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailWorkerService
{
    /// <summary>
    /// Manages bearer token lifecycle — obtains tokens using userId/password
    /// and automatically refreshes them before or upon expiry.
    /// </summary>
    public class TokenService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AuthSettings _authSettings;
        private readonly ILogger<TokenService> _logger;

        private string? _accessToken;
        private string? _refreshToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        // Refresh 60 seconds before actual expiry to avoid edge-case failures
        private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

        public TokenService(IHttpClientFactory httpClientFactory, AuthSettings authSettings, ILogger<TokenService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _authSettings = authSettings;
            _logger = logger;
        }

        /// <summary>
        /// Returns a valid access token. Automatically authenticates or refreshes as needed.
        /// </summary>
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            // If we have a valid, non-expired token, return it
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry - ExpiryBuffer)
            {
                return _accessToken;
            }

           

            // Authenticate with userId and password
            await AuthenticateAsync(cancellationToken);
            return _accessToken!;
        }

        /// <summary>
        /// Authenticates using email and password to obtain a new bearer token pair.
        /// </summary>
        private async Task AuthenticateAsync(CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();

            var requestBody = new
            {
                email = _authSettings.Email,
                password = _authSettings.Password
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_authSettings.BaseUrl.TrimEnd('/')}{_authSettings.TokenEndpoint}";
            _logger.LogInformation("Authenticating with token endpoint: {url}", url);

            var response = await client.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Authentication failed: no access token received.");
            }

            _accessToken = tokenResponse.AccessToken; 
            //_tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600);

            _logger.LogInformation("Authentication successful. Token expires at {expiry}", _tokenExpiry);
        }

        /// <summary>
        /// Attempts to refresh the access token using the stored refresh token.
        /// Returns true if successful, false otherwise.
        /// </summary>
       
    }

    /// <summary>
    /// Represents the auth server's token response.
    /// </summary>
    //public class TokenResponse
    //{
    //    [JsonPropertyName("access_token")]
    //    public string AccessToken { get; set; } = string.Empty;

    //    [JsonPropertyName("refresh_token")]
    //    public string? RefreshToken { get; set; }

    //    [JsonPropertyName("expires_in")]
    //    public int ExpiresIn { get; set; }

    //    [JsonPropertyName("token_type")]
    //    public string TokenType { get; set; } = "Bearer";
    //}

    public class TokenResponse
    {
        [JsonPropertyName("token")]
        public string AccessToken { get; set; } = string.Empty;

       
    }
}
