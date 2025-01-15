using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using ConnettoreRicett.Models;
using ConnettoreRicett.Services;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;
using Microsoft.Extensions.Options;


namespace ConnettoreRicett.Controllers
{
    public class RicettaController : Controller
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly Oauth2Service _oauth2Service;
        private readonly ILogger<RicettaController> _logger;
        private readonly TaxonomyService _taxonomyService;
        private readonly LocationHandler _locationHandler;
        private readonly LiferayConfiguration _liferayConfig;

        // Dizionario per i vocabolari di tassonomia
        private readonly Dictionary<string, int> _vocabularies;

        public RicettaController(
            IHttpClientFactory clientFactory,
            Oauth2Service oauth2Service,
            ILogger<RicettaController> logger,
            TaxonomyService taxonomyService,
            LocationHandler locationHandler,
            IOptions<LiferayConfiguration> liferayConfig)
        {
            _clientFactory = clientFactory;
            _oauth2Service = oauth2Service;
            _logger = logger;
            _taxonomyService = taxonomyService;
            _locationHandler = locationHandler;
            _liferayConfig = liferayConfig.Value;

            _vocabularies = new Dictionary<string, int>
            {
                { "Licenza", _liferayConfig.Vocabularies.Licenza },
                { "Categoria Ricetta", _liferayConfig.Vocabularies.CategoriaRicetta },
                { "Riferimento Geografico", _liferayConfig.Vocabularies.RiferimentoGeografico },
                { "Tema", _liferayConfig.Vocabularies.Tema }
            };
        }

        private async Task<(int id, string url)> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return (_liferayConfig.DefaultImage.Id, _liferayConfig.DefaultImage.Url);

            try
            {
                using var client = _clientFactory.CreateClient("LiferayClient");
                var token = await _oauth2Service.GetTokenAsync("ricetta");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var fileName = Path.GetFileNameWithoutExtension(file.FileName);
                var extension = Path.GetExtension(file.FileName);
                var finalFileName = file.FileName;

                // Controllo se esiste un file con lo stesso nome
                var checkUrl = $"/o/headless-delivery/v1.0/document-folders/{_liferayConfig.Folders.Documents}/documents?search={HttpUtility.UrlEncode(fileName)}";
                var checkResponse = await client.GetAsync(checkUrl);

                if (checkResponse.IsSuccessStatusCode)
                {
                    var checkContent = await checkResponse.Content.ReadAsStringAsync();
                    var existingDocuments = JsonConvert.DeserializeObject<DocumentResponseList<DocumentResponseInternal>>(checkContent);

                    if (existingDocuments?.TotalCount > 0)
                    {
                        var existingFile = existingDocuments.Items[0];

                        // Se la dimensione è diversa, trovo un nuovo nome
                        if (existingFile.SizeInBytes != file.Length)
                        {
                            _logger.LogInformation($"File {fileName} esiste ma ha dimensione diversa. Cerco nuovo nome...");
                            var counter = 1;
                            var found = false;

                            do
                            {
                                var newFileName = $"{fileName}({counter}){extension}";
                                var newCheckUrl = $"/o/headless-delivery/v1.0/document-folders/{_liferayConfig.Folders.Documents}/documents?search={HttpUtility.UrlEncode(newFileName)}";
                                var newCheckResponse = await client.GetAsync(newCheckUrl);

                                if (!newCheckResponse.IsSuccessStatusCode ||
                                    JsonConvert.DeserializeObject<DocumentResponseList<DocumentResponseInternal>>(
                                        await newCheckResponse.Content.ReadAsStringAsync())?.TotalCount == 0)
                                {
                                    finalFileName = newFileName;
                                    found = true;
                                    _logger.LogInformation($"Nuovo nome trovato: {newFileName}");
                                    break;
                                }

                                counter++;
                            } while (!found && counter <= 100);

                            if (!found)
                            {
                                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                                finalFileName = $"{fileName}_{timestamp}{extension}";
                                _logger.LogInformation($"Usando timestamp come fallback: {finalFileName}");
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"File {fileName} esiste con stessa dimensione. Riusando esistente.");
                            return (existingFile.Id, existingFile.ContentUrl);
                        }
                    }
                }

                // Carico il nuovo file
                using var content = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();
                using var streamContent = new StreamContent(fileStream);
                content.Add(streamContent, "file", finalFileName);

                var response = await client.PostAsync($"/o/headless-delivery/v1.0/document-folders/{_liferayConfig.Folders.Documents}/documents", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var documentResponse = JsonConvert.DeserializeObject<DocumentResponseInternal>(responseContent);
                    _logger.LogInformation($"File {finalFileName} caricato con successo");
                    return (documentResponse.Id, documentResponse.ContentUrl);
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Errore durante il caricamento dell'immagine: {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante la gestione del file {file.FileName}: {ex.Message}");
                throw;
            }
        }



        // Metodo per ottenere le categorie di un vocabolario specifico
        private async Task<List<TaxonomyCategory>> GetTaxonomyCategories(int vocabularyId)
        {
            using var client = _clientFactory.CreateClient("LiferayClient");
            var token = await _oauth2Service.GetTokenAsync("ricetta");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"/o/headless-admin-taxonomy/v1.0/taxonomy-vocabularies/{vocabularyId}/taxonomy-categories?flatten=true&pageSize=250");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var categories = JsonConvert.DeserializeObject<TaxonomyCategoryResponse>(content);

                if (vocabularyId == _vocabularies["Riferimento Geografico"])
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

                    return allCategories;
                }

                return categories.Items;
            }

            return new List<TaxonomyCategory>();
        }

        [HttpGet]
        public async Task<IActionResult> FormRicetta()
        {
            var ricetta = new Ricetta();

            // Carico tutte le categorie di ogni vocabolario e le aggiungo al modello della ricetta
            foreach (var vocabulary in _vocabularies)
            {
                ricetta.VocabularyCategories[vocabulary.Key] = await GetTaxonomyCategories(vocabulary.Value);
            }

            return View(ricetta);
        }

        [HttpPost]
        public async Task<IActionResult> InviaRicetta(Ricetta ricetta, IFormFile immaginePrincipaleFile, List<IFormFile> altraImmagineFiles)
        {
            try
            {
                var token = await _oauth2Service.GetTokenAsync("ricetta");
                var immaginePrincipale = await UploadImage(immaginePrincipaleFile);

                // Lista dei content fields base
                var contentFields = new List<object>
                {
                    new { name = "denominazioneField", contentFieldValue = new { data = ricetta.Denominazione ?? "" } },
                    new { name = "presentazioneField", contentFieldValue = new { data = !string.IsNullOrEmpty(ricetta.Presentazione) ? $"<p>{ricetta.Presentazione}</p>" : "" } },
                    new { name = "difficoltaField", contentFieldValue = new { data = ricetta.Difficolta ?? "" } },
                    new { name = "preparazioneField", contentFieldValue = new { data = !string.IsNullOrEmpty(ricetta.Preparazione) ? $"<p>{ricetta.Preparazione}</p>" : "" } },
                    new { name = "cotturaField", contentFieldValue = new { data = ricetta.Cottura ?? "" } },
                    new { name = "dosiField", contentFieldValue = new { data = ricetta.Dosi ?? "" } },
                    new { name = "costoField", contentFieldValue = new { data = ricetta.Costo ?? "" } },
                    new { name = "ingredientiField", contentFieldValue = new { data = ricetta.Ingredienti ?? "" } },
                    new { name = "latitudineField", contentFieldValue = new { data = ricetta.Latitudine ?? "" } },
                    new { name = "longitudineField", contentFieldValue = new { data = ricetta.Longitudine ?? "" } },
                    new { name = "areaDiInteresseField", contentFieldValue = new { data = JsonConvert.SerializeObject(ricetta.AreaDiInteresse) } },
                    new {
                        name = "immaginePrincipaleMedia",
                        contentFieldValue = new {
                            document = new {
                                contentUrl = immaginePrincipale.url,
                                id = immaginePrincipale.id
                            }
                        }
                    }
                };

                // Gestione delle immagini aggiuntive
                if (altraImmagineFiles != null)
                {
                    foreach (var file in altraImmagineFiles.Where(f => f != null && f.Length > 0))
                    {
                        var altraImmagine = await UploadImage(file);
                        contentFields.Add(new
                        {
                            name = "altraImmagineMedia",
                            contentFieldValue = new
                            {
                                document = new
                                {
                                    contentUrl = altraImmagine.url,
                                    id = altraImmagine.id
                                }
                            }
                        });
                    }
                }

                // Gestione aggiornata dei campi META
                if (ricetta.MetaFields == null || !ricetta.MetaFields.Any() ||
                    ricetta.MetaFields.All(m => string.IsNullOrWhiteSpace(m.Chiave) && string.IsNullOrWhiteSpace(m.Valore)))
                {
                    // Se non ci sono metafields o sono tutti vuoti, aggiungo un metaFieldset vuoto
                    contentFields.Add(new
                    {
                        name = "metaFieldset",
                        nestedContentFields = new object[] { }
                    });
                }
                else
                {
                    // Aggiungo solo i metafields che hanno almeno uno dei due campi popolati
                    foreach (var metaField in ricetta.MetaFields.Where(m =>
                        !string.IsNullOrWhiteSpace(m.Chiave) || !string.IsNullOrWhiteSpace(m.Valore)))
                    {
                        contentFields.Add(new
                        {
                            name = "metaFieldset",
                            nestedContentFields = new[]
                            {
                                new { name = "chiaveField", contentFieldValue = new { data = metaField.Chiave ?? "" } },
                                new { name = "valoreField", contentFieldValue = new { data = metaField.Valore ?? "" } }
                            }
                        });
                    }
                }

                // Raccolgo gli ID delle tassonomie
                var taxonomyCategoryIds = new List<int>();

                // Aggiungo i valori di default per Licenza e Tema
                taxonomyCategoryIds.Add(_liferayConfig.DefaultTaxonomies.LicenzaId);
                taxonomyCategoryIds.AddRange(_liferayConfig.DefaultTaxonomies.TemaIds);

                // Gestione categoria Ricetta 
                if (int.TryParse(Request.Form["RicettaCategory"].ToString(), out int ricettaId))
                {
                    taxonomyCategoryIds.Add(ricettaId);
                }

                // Aggiungo le altre categorie selezionate
                var selectedCategories = Request.Form["TaxonomyCategoryIds"]
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Select(int.Parse);
                taxonomyCategoryIds.AddRange(selectedCategories);

                // Creo il body per la POST
                var structuredContent = new
                {
                    contentFields,
                    contentStructureId = _liferayConfig.ContentStructure.Id,
                    externalReferenceCode = Guid.NewGuid().ToString(),
                    taxonomyCategoryIds,
                    title = ricetta.Denominazione
                };

                var jsonBody = JsonConvert.SerializeObject(structuredContent, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });

                // Invio della richiesta a Liferay
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var client = _clientFactory.CreateClient("LiferayClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await client.PostAsync(
                    $"/o/headless-delivery/v1.0/structured-content-folders/{_liferayConfig.Folders.StructuredContent}/structured-contents",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Ricetta {ricetta.Denominazione} inviata con successo");
                    return RedirectToAction("Success");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Errore durante l'invio della ricetta {ricetta.Denominazione}: {errorContent}");
                    ModelState.AddModelError("", $"Errore durante l'invio della ricetta: {errorContent}");
                    foreach (var vocabulary in _vocabularies)
                    {
                        ricetta.VocabularyCategories[vocabulary.Key] = await GetTaxonomyCategories(vocabulary.Value);
                    }
                    return View("FormRicetta", ricetta);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'invio della ricetta {ricetta.Denominazione}: {ex.Message}");
                ModelState.AddModelError("", $"Si è verificato un errore: {ex.Message}");
                foreach (var vocabulary in _vocabularies)
                {
                    ricetta.VocabularyCategories[vocabulary.Key] = await GetTaxonomyCategories(vocabulary.Value);
                }
                return View("FormRicetta", ricetta);
            }
        }

        private async Task<bool> InviaRicettaInterna(Ricetta ricetta, IFormFile immaginePrincipaleFile, List<IFormFile> altraImmagineFiles)
        {
            try
            {
                var token = await _oauth2Service.GetTokenAsync("ricetta");
                var immaginePrincipale = await UploadImage(immaginePrincipaleFile);

                // Uso un HashSet temporaneo per gestire i duplicati
                var taxonomyIds = new HashSet<int>();

                // Aggiungo le tassonomie di default
                taxonomyIds.Add(_liferayConfig.DefaultTaxonomies.LicenzaId);

                // Aggiungo gli ID del tema
                foreach (var temaId in _liferayConfig.DefaultTaxonomies.TemaIds)
                {
                    taxonomyIds.Add(temaId);
                }

                // Aggiungo le tassonomie esistenti della ricetta
                if (ricetta.TaxonomyCategoryIds != null)
                {
                    foreach (var id in ricetta.TaxonomyCategoryIds)
                    {
                        taxonomyIds.Add(id);
                    }
                }

                // Processo le località e ottieni i dati geografici
                var locationResult = await _locationHandler.ProcessLocationsAsync(ricetta.RiferimentoGeograficoString);
                if (locationResult.Success)
                {
                    ricetta.Latitudine = locationResult.FirstLatitude.ToString("0.######", CultureInfo.InvariantCulture);
                    ricetta.Longitudine = locationResult.FirstLongitude.ToString("0.######", CultureInfo.InvariantCulture);
                    ricetta.AreaDiInteresse = locationResult.AreaInteresseGeoJson;
                    // Aggiungo gli ID delle località al HashSet
                    foreach (var id in locationResult.TaxonomyIds)
                    {
                        taxonomyIds.Add(id);
                    }
                }
                else
                {
                    _logger.LogWarning($"Impossibile processare le località per la ricetta {ricetta.Denominazione}");
                }

                // Aggiorno la lista delle tassonomie della ricetta
                ricetta.TaxonomyCategoryIds = new List<int>(taxonomyIds);

                var contentFields = new List<object>
                {
                    new { name = "denominazioneField", contentFieldValue = new { data = ricetta.Denominazione ?? "" } },
                    new { name = "presentazioneField", contentFieldValue = new { data = !string.IsNullOrEmpty(ricetta.Presentazione) ? $"<p>{ricetta.Presentazione}</p>" : "" } },
                    new { name = "difficoltaField", contentFieldValue = new { data = ricetta.Difficolta ?? "" } },
                    new { name = "preparazioneField", contentFieldValue = new { data = !string.IsNullOrEmpty(ricetta.Preparazione) ? $"<p>{ricetta.Preparazione}</p>" : "" } },
                    new { name = "cotturaField", contentFieldValue = new { data = ricetta.Cottura ?? "" } },
                    new { name = "dosiField", contentFieldValue = new { data = ricetta.Dosi ?? "" } },
                    new { name = "costoField", contentFieldValue = new { data = ricetta.Costo ?? "" } },
                    new { name = "ingredientiField", contentFieldValue = new { data = ricetta.Ingredienti ?? "" } },
                    new { name = "latitudineField", contentFieldValue = new { data = ricetta.Latitudine ?? "" } },
                    new { name = "longitudineField", contentFieldValue = new { data = ricetta.Longitudine ?? "" } },
                    new { name = "areaDiInteresseField", contentFieldValue = new { data = JsonConvert.SerializeObject(ricetta.AreaDiInteresse) } },
                    new {
                        name = "immaginePrincipaleMedia",
                        contentFieldValue = new {
                            document = new {
                                contentUrl = immaginePrincipale.url,
                                id = immaginePrincipale.id
                            }
                        }
                    }
                };

                // Aggiunta del metaFieldset vuoto
                contentFields.Add(new
                {
                    name = "metaFieldset",
                    nestedContentFields = new object[] { }
                });

                var structuredContent = new
                {
                    contentFields,
                    contentStructureId = _liferayConfig.ContentStructure.Id,
                    externalReferenceCode = Guid.NewGuid().ToString(),
                    taxonomyCategoryIds = ricetta.TaxonomyCategoryIds,
                    title = ricetta.Denominazione
                };

                var jsonBody = JsonConvert.SerializeObject(structuredContent, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });

                _logger.LogInformation($"JSON body inviato a Liferay: {jsonBody}");

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var client = _clientFactory.CreateClient("LiferayClient");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await client.PostAsync($"/o/headless-delivery/v1.0/structured-content-folders/{_liferayConfig.Folders.StructuredContent}/structured-contents", content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Ricetta {ricetta.Denominazione} inviata con successo");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Errore durante l'invio della ricetta {ricetta.Denominazione}: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore durante l'invio della ricetta {ricetta.Denominazione}: {ex.Message}");
                return false;
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportaExcel(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                _logger.LogError("File non caricato o vuoto");
                return RedirectToAction("Error");
            }

            _logger.LogInformation($"Inizio elaborazione file: {excelFile.FileName}, dimensione: {excelFile.Length} bytes");
            var successCount = 0;
            var errorCount = 0;

            try
            {
                using (var reader = new StreamReader(excelFile.OpenReadStream()))
                {
                    _logger.LogInformation("File aperto con successo");
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        Delimiter = ";",
                        Quote = '"',
                        MissingFieldFound = null
                    };

                    using (var csv = new CsvReader(reader, config))
                    {
                        try
                        {
                            csv.Read();
                            csv.ReadHeader();
                            var headers = csv.HeaderRecord;
                            _logger.LogInformation($"Headers letti: {string.Join(", ", headers)}");

                            int currentRow = 2;
                            while (csv.Read())
                            {
                                _logger.LogInformation($"Elaborazione riga {currentRow}");
                                try
                                {
                                    var ricetta = new Ricetta
                                    {
                                        Denominazione = csv.GetField("Denominazione")?.Trim(),
                                        Presentazione = csv.GetField("Presentazione")?.Trim(),
                                        Difficolta = csv.GetField("Difficolta")?.Trim(),
                                        Preparazione = csv.GetField("Preparazione")?.Trim(),
                                        Cottura = csv.GetField("Cottura")?.Trim(),
                                        Dosi = csv.GetField("Dosi")?.Trim(),
                                        Costo = csv.GetField("Costo")?.Trim(),
                                        Ingredienti = csv.GetField("Ingredienti")?.Trim(),
                                        RiferimentoGeograficoString = csv.GetField("RiferimentoGeografico")?.Trim(),
                                        TaxonomyCategoryIds = new List<int>()
                                    };

                                    // Gestisco le tassonomie senza duplicati
                                    var taxonomyIds = new HashSet<int>();

                                    // Aggiungo la categoria ricetta
                                    var catRicetta = csv.GetField("CategoriaRicetta")?.Trim();
                                    if (!string.IsNullOrEmpty(catRicetta))
                                    {
                                        var ricettaCategories = await _taxonomyService.GetTaxonomyMappings(
                                            _liferayConfig.Vocabularies.CategoriaRicetta.ToString());
                                        if (ricettaCategories.TryGetValue(catRicetta, out int catId))
                                        {
                                            taxonomyIds.Add(catId);
                                        }
                                    }

                                    // Processo il riferimento geografico
                                    if (!string.IsNullOrEmpty(ricetta.RiferimentoGeograficoString))
                                    {
                                        var locationResult = await _locationHandler.ProcessLocationsAsync(ricetta.RiferimentoGeograficoString);
                                        if (locationResult.Success)
                                        {
                                            ricetta.Latitudine = locationResult.FirstLatitude.ToString(CultureInfo.InvariantCulture);
                                            ricetta.Longitudine = locationResult.FirstLongitude.ToString(CultureInfo.InvariantCulture);
                                            ricetta.AreaDiInteresse = locationResult.AreaInteresseGeoJson;

                                            foreach (var id in locationResult.TaxonomyIds)
                                            {
                                                taxonomyIds.Add(id);
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogWarning($"Impossibile processare le località per la ricetta {ricetta.Denominazione}");
                                        }
                                    }

                                    // Aggiungo i valori di default
                                    taxonomyIds.Add(_liferayConfig.DefaultTaxonomies.LicenzaId);

                                    // Aggiungo gli ID del tema
                                    foreach (var temaId in _liferayConfig.DefaultTaxonomies.TemaIds)
                                    {
                                        taxonomyIds.Add(temaId);
                                    }

                                    // Converto HashSet in List e lo assegno alla ricetta
                                    ricetta.TaxonomyCategoryIds = taxonomyIds.ToList();

                                    _logger.LogInformation($"Invio ricetta: {ricetta.Denominazione}");
                                    var result = await InviaRicettaInterna(ricetta, null, null);
                                    if (result)
                                    {
                                        _logger.LogInformation($"Ricetta inviata con successo: {ricetta.Denominazione}");
                                        successCount++;
                                    }
                                    else
                                    {
                                        _logger.LogError($"Errore nell'invio della ricetta: {ricetta.Denominazione}");
                                        errorCount++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    errorCount++;
                                    _logger.LogError($"Errore nell'elaborazione della riga {currentRow}: {ex.Message}\n{ex.StackTrace}");
                                }
                                currentRow++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Errore durante la lettura CSV: {ex.Message}\n{ex.StackTrace}");
                            return RedirectToAction("Error");
                        }
                    }
                }

                TempData["SuccessMessage"] = $"Importazione completata. Ricette importate con successo: {successCount}. Errori: {errorCount}";
                return RedirectToAction("Success");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Errore generale: {ex.Message}\n{ex.StackTrace}");
                return RedirectToAction("Error");
            }
        }

        public IActionResult Success()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
