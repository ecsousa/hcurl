using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace hcurl
{
    class CachedToken {
        public string AccessToken { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }

    [JsonSerializable(typeof(Dictionary<string, CachedToken>))]
    partial class TokenCacheJsonContext : JsonSerializerContext { }

    // Obtains OAuth tokens using the client credentials grant, caching them on disk until they expire.
    class TokenResolver {

        private static readonly TimeSpan expiryMargin = TimeSpan.FromSeconds(60);
        private static readonly HttpClient httpClient = new HttpClient();

        private readonly string cacheDirectory = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hcurl");
        private string CacheFile => Path.Join(this.cacheDirectory, "token-cache.json");

        public async Task<string> GetTokenAsync(
            string rawExpression,
            Func<Task<(string url, string clientId, string clientSecret, string scope)>> resolveArgs) {

            // Keyed by the unresolved expression, so a cache hit doesn't need to touch 1Password.
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawExpression)));
            var useCache = Environment.GetEnvironmentVariable("HCURL_NO_CACHE") != "1";

            if (useCache && this.LoadCache().TryGetValue(key, out var cached) && cached.ExpiresAt - expiryMargin > DateTimeOffset.UtcNow)
                return cached.AccessToken;

            var args = await resolveArgs();
            var (accessToken, expiresIn) = await RequestTokenAsync(args.url, args.clientId, args.clientSecret, args.scope);

            if (expiresIn != null) {
                var cache = this.LoadCache();
                cache[key] = new CachedToken { AccessToken = accessToken, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value) };
                this.SaveCache(cache);
            }

            return accessToken;
        }

        private static async Task<(string accessToken, long? expiresIn)> RequestTokenAsync(string url, string clientId, string clientSecret, string scope) {
            var content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = scope,
            });

            HttpResponseMessage response;
            try {
                response = await httpClient.PostAsync(url, content);
            }
            catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException || e is UriFormatException) {
                throw new HcurlException($"Token request to {url} failed: {e.Message}");
            }

            using (response) {
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new HcurlException($"Token request to {url} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

                try {
                    using var json = JsonDocument.Parse(body);
                    var root = json.RootElement;

                    if (!root.TryGetProperty("access_token", out var tokenElement) || tokenElement.ValueKind != JsonValueKind.String)
                        throw new HcurlException($"Token response from {url} has no access_token");

                    long? expiresIn = null;
                    if (root.TryGetProperty("expires_in", out var expiresElement)) {
                        if (expiresElement.ValueKind == JsonValueKind.Number && expiresElement.TryGetInt64(out var n))
                            expiresIn = n;
                        else if (expiresElement.ValueKind == JsonValueKind.String && long.TryParse(expiresElement.GetString(), out var s))
                            expiresIn = s;
                    }

                    return (tokenElement.GetString()!, expiresIn);
                }
                catch (JsonException e) {
                    throw new HcurlException($"Token response from {url} is not valid JSON: {e.Message}");
                }
            }
        }

        private Dictionary<string, CachedToken> LoadCache() {
            try {
                if (File.Exists(this.CacheFile)) {
                    var cache = JsonSerializer.Deserialize(File.ReadAllText(this.CacheFile), TokenCacheJsonContext.Default.DictionaryStringCachedToken);
                    if (cache != null)
                        return cache;
                }
            }
            catch (Exception e) when (e is JsonException || e is IOException) {
                // A corrupt or unreadable cache is treated as empty.
            }

            return new Dictionary<string, CachedToken>();
        }

        private void SaveCache(Dictionary<string, CachedToken> cache) {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in cache.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToList())
                cache.Remove(expired);

            var tempFile = this.CacheFile + ".tmp";
            var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };

            if (OperatingSystem.IsWindows()) {
                Directory.CreateDirectory(this.cacheDirectory);
            }
            else {
                Directory.CreateDirectory(this.cacheDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            // Make sure the temp file is freshly created, so it gets the restricted permissions.
            File.Delete(tempFile);

            using (var stream = new FileStream(tempFile, fileOptions)) {
                JsonSerializer.Serialize(stream, cache, TokenCacheJsonContext.Default.DictionaryStringCachedToken);
            }

            File.Move(tempFile, this.CacheFile, overwrite: true);
        }

    }
}
