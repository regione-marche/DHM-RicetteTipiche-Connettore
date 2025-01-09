using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using ConnettoreRicett.Models;

namespace ConnettoreRicett.Services
{
    public class LocationHandler
    {
        private readonly NominatimService _nominatimService;
        private readonly TaxonomyService _taxonomyService;
        private readonly ILogger<LocationHandler> _logger;
        private readonly LiferayConfiguration _liferayConfig;

        public class LocationProcessResult
        {
            public List<int> TaxonomyIds { get; set; } = new List<int>();
            public double FirstLatitude { get; set; }
            public double FirstLongitude { get; set; }
            public string AreaInteresseGeoJson { get; set; }
            public bool Success { get; set; }
        }

        public LocationHandler(
            NominatimService nominatimService,
            TaxonomyService taxonomyService,
            ILogger<LocationHandler> logger,
            IOptions<LiferayConfiguration> liferayConfig)
        {
            _nominatimService = nominatimService;
            _taxonomyService = taxonomyService;
            _logger = logger;
            _liferayConfig = liferayConfig.Value;
        }

        public async Task<LocationProcessResult> ProcessLocationsAsync(string locationsString)
        {
            var result = new LocationProcessResult();
            var features = new List<object>();
            var processedLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Ottiene il mapping delle località dalle tassonomie usando l'ID dalla configurazione
                var vocabularyId = _liferayConfig.Vocabularies.RiferimentoGeografico;
                var taxonomyMappings = await _taxonomyService.GetTaxonomyMappings(vocabularyId.ToString());

                // Split delle località
                var locations = locationsString
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase) // Rimuove duplicati
                    .ToList();

                foreach (var location in locations)
                {
                    if (processedLocations.Contains(location))
                    {
                        _logger.LogInformation($"Località già processata, salto: {location}");
                        continue;
                    }

                    // Cerca l'ID della tassonomia per questa località
                    if (taxonomyMappings.TryGetValue(location, out int taxonomyId))
                    {
                        if (!result.TaxonomyIds.Contains(taxonomyId))
                        {
                            result.TaxonomyIds.Add(taxonomyId);
                            processedLocations.Add(location);

                            // Ottiene i dati geografici
                            var locationData = await _nominatimService.GetLocationDataAsync(location);
                            if (locationData.HasValue)
                            {
                                // Salva le coordinate della prima località
                                if (features.Count == 0)
                                {
                                    result.FirstLatitude = locationData.Value.lat;
                                    result.FirstLongitude = locationData.Value.lon;
                                }

                                // Crea il Feature GeoJSON
                                var feature = new
                                {
                                    type = "Feature",
                                    properties = new { name = location },
                                    geometry = new
                                    {
                                        type = "Point",
                                        coordinates = new[] {
                                            locationData.Value.lon,
                                            locationData.Value.lat
                                        }
                                    }
                                };
                                features.Add(feature);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Nessuna tassonomia trovata per la località: {location}");
                    }
                }

                // Crea il FeatureCollection GeoJSON
                var featureCollection = new
                {
                    type = "FeatureCollection",
                    features = features
                };

                // Serializza direttamente l'oggetto GeoJSON
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false // per un JSON più compatto
                };

                result.AreaInteresseGeoJson = JsonSerializer.Serialize(featureCollection, options);
                result.Success = result.TaxonomyIds.Any(); // Success se almeno una località è stata processata
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante il processing delle località");
                result.Success = false;
            }

            return result;
        }
    }
}