using IdentityModel.Client;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace ConnettoreRicett.Services
{
    public class OAuth2Settings
    {
        public string TokenUrl { get; set; }
        public string ClientID { get; set; }
        public string ClientSecret { get; set; }
    }

    public class Oauth2Service
    {
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, TokenInfo> _tokens;
        private readonly OAuth2Settings _settings;

        public Oauth2Service(HttpClient httpClient, IOptions<OAuth2Settings> settings)
        {
            _httpClient = httpClient;
            _tokens = new ConcurrentDictionary<string, TokenInfo>();
            _settings = settings.Value;
        }

        public async Task<string> GetTokenAsync(string target)
        {
            if (_tokens.TryGetValue(target, out var tokenInfo) && DateTime.UtcNow < tokenInfo.ExpiryTime)
            {
                return tokenInfo.Token;
            }
            return await GenerateTokenAsync(target);
        }

        private async Task<string> GenerateTokenAsync(string target)
        {
            var tokenResponse = await _httpClient.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = _settings.TokenUrl,
                ClientId = _settings.ClientID,
                ClientSecret = _settings.ClientSecret,
                ClientCredentialStyle = ClientCredentialStyle.PostBody
            });

            if (tokenResponse.IsError)
            {
                throw new Exception(tokenResponse.Error);
            }

            var tokenInfo = new TokenInfo
            {
                Token = tokenResponse.AccessToken,
                ExpiryTime = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60)
            };

            _tokens[target] = tokenInfo;
            return tokenResponse.AccessToken;
        }

        private class TokenInfo
        {
            public string Token { get; set; }
            public DateTime ExpiryTime { get; set; }
        }
    }
}