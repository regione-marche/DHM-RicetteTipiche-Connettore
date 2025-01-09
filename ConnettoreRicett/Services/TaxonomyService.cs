using ConnettoreRicett.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace ConnettoreRicett.Services
{
    public class TaxonomyService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly Oauth2Service _oauth2Service;
        private readonly LiferayConfiguration _liferayConfig;
        private readonly ConcurrentDictionary<string, Dictionary<string, int>> _taxonomyCache;

        public TaxonomyService(
            IHttpClientFactory clientFactory,
            Oauth2Service oauth2Service,
            IOptions<LiferayConfiguration> liferayConfig)
        {
            _clientFactory = clientFactory;
            _oauth2Service = oauth2Service;
            _liferayConfig = liferayConfig.Value;
            _taxonomyCache = new ConcurrentDictionary<string, Dictionary<string, int>>();
        }
        public async Task<Dictionary<string, int>> GetTaxonomyMappings(string vocabularyId)
        {
            // Provo a ottenere dalla cache
            if (_taxonomyCache.TryGetValue(vocabularyId, out var cachedMappings))
            {
                return cachedMappings;
            }

            using var client = _clientFactory.CreateClient("LiferayClient");
            var token = await _oauth2Service.GetTokenAsync("ricetta");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"/o/headless-admin-taxonomy/v1.0/taxonomy-vocabularies/{vocabularyId}/taxonomy-categories?flatten=true&pageSize=250");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var categories = JsonConvert.DeserializeObject<TaxonomyCategoryResponse>(content);
                var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Se è il vocabolario del Riferimento Geografico
                if (vocabularyId == _liferayConfig.Vocabularies.RiferimentoGeografico.ToString())
                {
                    var allCategories = new List<TaxonomyCategory>();
                    var mainCategories = categories.Items.Where(c => c.ParentCategoryId == null).ToList();

                    // Per ogni categoria principale (provincia)
                    foreach (var mainCategory in mainCategories)
                    {
                        // Ottengo tutte le sottocategorie ricorsivamente
                        var subResponse = await client.GetAsync($"/o/headless-admin-taxonomy/v1.0/taxonomy-categories/{mainCategory.Id}/taxonomy-categories?flatten=true&pageSize=250");

                        if (subResponse.IsSuccessStatusCode)
                        {
                            var subContent = await subResponse.Content.ReadAsStringAsync();
                            var subCategories = JsonConvert.DeserializeObject<TaxonomyCategoryResponse>(subContent);
                            if (subCategories?.Items != null)
                            {
                                allCategories.AddRange(subCategories.Items);
                            }
                        }
                    }

                    // Uso solo le sottocategorie (comuni) per il mapping
                    foreach (var category in allCategories)
                    {
                        var normalizedName = NormalizeName(category.Name);
                        mappings[normalizedName] = category.Id;

                        // Aggiungo anche il nome originale
                        if (!mappings.ContainsKey(category.Name))
                        {
                            mappings[category.Name] = category.Id;
                        }
                    }
                }
                else
                {
                    // Per altri vocabolari, uso il comportamento normale
                    foreach (var category in categories.Items)
                    {
                        var normalizedName = NormalizeName(category.Name);
                        mappings[normalizedName] = category.Id;

                        // Aggiungo anche il nome originale
                        if (!mappings.ContainsKey(category.Name))
                        {
                            mappings[category.Name] = category.Id;
                        }
                    }
                }

                // Salvo in cache
                _taxonomyCache.TryAdd(vocabularyId, mappings);
                return mappings;
            }

            throw new Exception($"Impossibile ottenere le categorie per il vocabolario {vocabularyId}");
        }

        private string NormalizeName(string name)
        {
            // Rimuovo accenti
            string normalized = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            // Converto in minuscolo e rimuovo caratteri speciali
            return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"[^a-z0-9\s]", "");
        }

        public async Task<List<int>> GetTaxonomyIds(
                    string riferimentoGeografico,
                    string categoriaRicetta,
                    Dictionary<string, int> vocabularyIds)
        {
            var result = new List<int>();

            // Aggiungo le tassonomie di default
            result.Add(_liferayConfig.DefaultTaxonomies.LicenzaId);
            result.AddRange(_liferayConfig.DefaultTaxonomies.TemaIds);

            // Mappa per ogni vocabolario
            var mappings = new Dictionary<string, Dictionary<string, int>>();
            foreach (var vocab in vocabularyIds)
            {
                mappings[vocab.Key] = await GetTaxonomyMappings(vocab.Value.ToString());
            }

            // Cerca gli ID per ogni categoria
            if (!string.IsNullOrEmpty(riferimentoGeografico) &&
                mappings["Riferimento Geografico"].TryGetValue(riferimentoGeografico, out int geoId))
            {
                result.Add(geoId);
            }

            if (!string.IsNullOrEmpty(categoriaRicetta) &&
                mappings["Categoria Ricetta"].TryGetValue(categoriaRicetta, out int catId))
            {
                result.Add(catId);
            }

            return result;
        }
    }
}