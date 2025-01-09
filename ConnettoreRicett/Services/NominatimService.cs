using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConnettoreRicett.Services
{
    public class NominatimService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<NominatimService> _logger;
        private readonly SemaphoreSlim _throttler;
        private readonly Dictionary<string, (double lat, double lon, string geojson)> _cache;
        private DateTime _lastRequestTime;
        private const int MIN_DELAY_MS = 3000;

        public NominatimService(IHttpClientFactory clientFactory, ILogger<NominatimService> logger)
        {
            _clientFactory = clientFactory;
            _logger = logger;
            _throttler = new SemaphoreSlim(1, 1);
            _cache = new Dictionary<string, (double lat, double lon, string geojson)>();
            _lastRequestTime = DateTime.MinValue;
        }

        public async Task<(double lat, double lon, string geojson)?> GetLocationDataAsync(string locationName)
        {
            // Check cache first
            if (_cache.TryGetValue(locationName, out var cachedData))
            {
                _logger.LogInformation($"Cache hit for location: {locationName}");
                return cachedData;
            }

            await _throttler.WaitAsync();
            try
            {
                // Ensure minimum delay between requests
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest.TotalMilliseconds < MIN_DELAY_MS)
                {
                    await Task.Delay(MIN_DELAY_MS - (int)timeSinceLastRequest.TotalMilliseconds);
                }

                var query = $"{locationName}, Marche, Italia";
                var url = $"https://nominatim.openstreetmap.org/search?format=json&q={Uri.EscapeDataString(query)}&limit=1";

                var client = _clientFactory.CreateClient("NominatimClient");
                var response = await client.GetAsync(url);
                _lastRequestTime = DateTime.UtcNow;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Failed to get location data for {locationName}. Status: {response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var results = JsonSerializer.Deserialize<List<NominatimResponse>>(content);

                if (results == null || !results.Any())
                {
                    _logger.LogWarning($"No results found for location: {locationName}");
                    return null;
                }

                var result = results.First();

                // Parsing esplicito con CultureInfo.InvariantCulture
                var lat = double.Parse(result.lat, CultureInfo.InvariantCulture);
                var lon = double.Parse(result.lon, CultureInfo.InvariantCulture);

                // Log per debug
                _logger.LogInformation($"Parsed coordinates for {locationName}: lat={lat}, lon={lon}");

                var locationData = (
                    lat: lat,
                    lon: lon,
                    geojson: JsonSerializer.Serialize(new
                    {
                        type = "Feature",
                        properties = new { name = locationName },
                        geometry = new
                        {
                            type = "Point",
                            coordinates = new[] { lon, lat }
                        }
                    }, new JsonSerializerOptions
                    {
                        // Opzioni di serializzazione per garantire la precisione dei numeri
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
                        WriteIndented = false
                    })
                );

                // Verifica che le coordinate siano nei range validi
                if (ValidateCoordinates(lat, lon))
                {
                    // Cache the result
                    _cache[locationName] = locationData;
                    return locationData;
                }
                else
                {
                    _logger.LogError($"Invalid coordinates for {locationName}: lat={lat}, lon={lon}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting location data for {locationName}");
                return null;
            }
            finally
            {
                _throttler.Release();
            }
        }

        private bool ValidateCoordinates(double lat, double lon)
        {
            return lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
        }

        private class NominatimResponse
        {
            public string lat { get; set; }
            public string lon { get; set; }
            public string display_name { get; set; }
        }
    }
}