using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DataLayer.Models.MilAnuncios;
using System.Threading.Tasks;
using System.Net;
using AutoMapper;
using DataLayer;
using DataLayer.Models;
using DataLayer.Models.PostGresModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ScrapperGateway.Models.Wallapop;
using ServicesLayer;
using DataLayer.Models.DTOs;
using Amazon.Runtime.Internal;
using System.Threading;

// Clase para almacenar el formato liviano de los anuncios
public class AdLight
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public double Price { get; set; }
}

public class Categorys
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class WebMixerService : IWebMixerService
{
    private readonly IWeb1Data _web1Data;
    private readonly IWeb2Data _web2Data;
    private readonly IWeb3Data _web3Data;
    private readonly IMapper _mapper;
    private readonly WebClient _webClient;
    private const string GROK_API_KEY = "REEMPLAZAR";
    private static readonly SemaphoreSlim _requestSemaphore = new SemaphoreSlim(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan _minRequestInterval = TimeSpan.FromSeconds(1.5);
    private List<Categorys> categorias;
    private List<AdLight> potentialDeals;
    private List<AdModel> allAdsList;
    private readonly IConfiguration _configuration;
    private AppDbContext _context;
    private string categoryString = "";

    public WebMixerService(
     IWeb1Data web1Data,
     IWeb2Data web2Data,
     IWeb3Data web3Data,
     IMapper mapper,
     IConfiguration configuration,
     AppDbContext appDbContext)
    {
        _context = appDbContext;
        _web1Data = web1Data;
        _web2Data = web2Data;
        _web3Data = web3Data;
        _mapper = mapper;
        _webClient = new WebClient();
        potentialDeals = new();
        _configuration = configuration; // Asegúrate de almacenar configuration en un campo privado
        Console.WriteLine($"WebMixerService inicializado. Entorno: {_configuration["ASPNETCORE_ENVIRONMENT"]}");
    }

    //funcion busqueda general
    public async Task<List<AdModel>> Search(SearchRequestDto request)
    {
        try
        {
            Console.WriteLine($"Iniciando búsqueda con solicitud: Palabras clave={request.Keywords}, Categoría={request.Category}, Plataformas={string.Join(",", request.PlatformIds)}");

            var category = await _context.Categories.FirstOrDefaultAsync(z => z.Id == request.Category);
            if (category == null)
            {
                Console.WriteLine($"No se encontró la categoría con ID {request.Category}.");
                return new List<AdModel>();
            }
            categoryString = category.Name;
            Console.WriteLine($"Categoría encontrada: {categoryString}");

            // Obtener todos los anuncios
            Console.WriteLine("Obteniendo anuncios...");
            allAdsList = await GetAds(request);
            Console.WriteLine($"Total de anuncios obtenidos: {allAdsList.Count}");

            List<AdModel> listaAnuncios = [];

            var adsLight = MapAdsToLightFormat(allAdsList);
            Console.WriteLine($"Mapeados {adsLight.Count} anuncios a formato ligero");

            // Dividir los anuncios en lotes para enviarlos a la IA
            var batches = SplitAdsIntoBatches(adsLight);
            Console.WriteLine($"Divididos los anuncios en {batches.Count} lotes");

            // Obtener las puntuaciones de los anuncios desde la IA
            Console.WriteLine("Enviando lotes a la IA para puntuación...");
            var scores = await GetAdScoresFromAI(batches, request.UserSearch, request.Keywords, request.Category, request.IsProgrammed);
            Console.WriteLine($"Recibidos {scores.Count} anuncios puntuados de la IA");

            // Filtrar los mejores anuncios según las puntuaciones
            listaAnuncios = GetBestAds(adsLight, scores);
            Console.WriteLine($"Filtrados los mejores anuncios: {listaAnuncios.Count}");

            List<Ad> listadomapeado = _mapper.Map<List<Ad>>(listaAnuncios);
            Console.WriteLine($"Mapeados {listadomapeado.Count} anuncios a entidades Ad");

            // Filtrar anuncios únicos antes de agregarlos
            var uniqueAds = listadomapeado
                .GroupBy(ad => ad.Id)
                .Select(g => g.First())
                .ToList();
            Console.WriteLine($"Filtrados {uniqueAds.Count} anuncios únicos");

            // Obtener IDs de los anuncios que ya existen en la base de datos
            var existingAdIds = await _context.Ads
                .Where(ad => uniqueAds.Select(l => l.Id).Contains(ad.Id))
                .Where(ad => !string.IsNullOrEmpty(ad.Slug))
                .Select(ad => ad.Id)
                .ToListAsync();
            Console.WriteLine($"Encontrados {existingAdIds.Count} anuncios existentes en la base de datos");

            // Filtrar los anuncios nuevos que no están en la base de datos
            var newAds = uniqueAds.Where(ad => !existingAdIds.Contains(ad.Id)).ToList();
            Console.WriteLine($"Identificados {newAds.Count} anuncios nuevos para agregar a la base de datos");

            // Agregar solo los anuncios nuevos a la base de datos
            if (newAds.Any())
            {
                await _context.Ads.AddRangeAsync(newAds);
                var savedCount = await _context.SaveChangesAsync();
                Console.WriteLine($"Guardados {savedCount} anuncios nuevos en la base de datos");
            }

            return listaAnuncios;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en la búsqueda: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            throw;
        }
    }

    public List<AdLight> MapAdsToLightFormat(List<AdModel> allAdsList)
    {
        try
        {
            Console.WriteLine("Mapeando anuncios a formato ligero...");
            const int MAX_DESCRIPTION_LENGTH = 500;

            var result = allAdsList.Select(ad => new AdLight
            {
                Id = ad.id,
                Title = ad.title,
                Description = ad.description?.Length > MAX_DESCRIPTION_LENGTH
                    ? ad.description.Substring(0, MAX_DESCRIPTION_LENGTH) + "..."
                    : ad.description ?? string.Empty,
                Price = ad.price
            }).ToList();

            Console.WriteLine($"Mapeados correctamente {result.Count} anuncios a formato ligero");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en MapAdsToLightFormat: {ex.Message}");
            throw;
        }
    }

    public List<List<AdLight>> SplitAdsIntoBatches(List<AdLight> ads, int batchSize = 15)
    {
        try
        {
            Console.WriteLine($"Dividiendo {ads.Count} anuncios en lotes de {batchSize}");
            var batches = ads.Select((ad, index) => new { ad, index })
                            .GroupBy(x => x.index / batchSize)
                            .Select(group => group.Select(x => x.ad).ToList())
                            .ToList();
            Console.WriteLine($"Creados {batches.Count} lotes");
            return batches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en SplitAdsIntoBatches: {ex.Message}");
            throw;
        }
    }

    public string GeneratePrompt(List<AdLight> adsBatch, string userSearch, string keywords, int? category, bool isProgrammed)
    {
        try
        {
            Console.WriteLine($"Generando prompt para lote de {adsBatch.Count} anuncios");
            var adsString = string.Join("\n", adsBatch.Select(ad =>
                $"ID: {ad.Id}\nTítulo: {ad.Title}\nDescripción: {ad.Description}\nPrecio: {ad.Price}€\n"));

            var prompt = $@"
            Eres un filtro inteligente de anuncios. Tu tarea es seleccionar únicamente los anuncios que **coincidan exactamente** con lo que el usuario está buscando. 

            ### 📌 **Reglas de filtrado:**
            1️⃣ **Categoría específica:** El usuario busca una **{categoryString}**, si el anuncio es de una pieza, despiece, repuesto o accesorio, **descártalo**.  
            2️⃣ **Coincidencia exacta:** Si el anuncio **no menciona explícitamente** lo que el usuario busca, **descártalo**.  
            3️⃣ **Precio relevante:** Si el usuario ha indicado un precio y el anuncio no lo menciona o es diferente, **descártalo**.  
            4️⃣ **Idioma:** La respuesta debe estar **en español**.  

            ### 🔍 **Lo que el usuario busca:**  
            - Palabras clave: **{keywords}**  
            - Búsqueda detallada: **{userSearch}**  

            ### 📢 **Anuncios disponibles:**  
            {adsString}

            ### 🔹 **Formato de respuesta:**  
            Devuelve los resultados en **JSON** con la siguiente estructura:  

            ```json
            [
              {{
                ""Ad ID"": ""<id>"",
                ""Score"": <puntuación>,
                ""Positivos"": [""aspecto positivo 1"", ""aspecto positivo 2""],
                ""Negativos"": [""aspecto negativo 1"", ""aspecto negativo 2""]
              }}
            ]
            ```

            Si ningún anuncio coincide, responde con:
            []
            ";

            Console.WriteLine("Prompt generado correctamente");
            return prompt;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en GeneratePrompt: {ex.Message}");
            throw;
        }
    }

    public async Task<List<(string Id, int Score, List<string> Positives, List<string> Negatives)>> GetAdScoresFromAI(
        List<List<AdLight>> adBatches, string userSearch, string keywords, int? category, bool isProgrammed)
    {
        var results = new List<(string Id, int Score, List<string> Positives, List<string> Negatives)>();
        Console.WriteLine($"Procesando {adBatches.Count} lotes para puntuación de IA");

        foreach (var batch in adBatches)
        {
            try
            {
                Console.WriteLine($"Procesando lote con {batch.Count} anuncios");
                var prompt = GeneratePrompt(batch, userSearch, keywords, category, isProgrammed);
                Console.WriteLine("Prompt generado, enviando a la IA...");

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        Console.WriteLine($"Intento {attempt} para el lote");
                        var response = await SendOpenAIRequestAsync(prompt);
                        Console.WriteLine($"Respuesta recibida de la IA: {response.Substring(0, Math.Min(100, response.Length))}...");

                        if (!string.IsNullOrEmpty(response))
                        {
                            var parsedResults = ParseAiResponse(response);
                            results.AddRange(parsedResults);
                            Console.WriteLine($"Parseados {parsedResults.Count} resultados de la respuesta de la IA");
                            break;
                        }

                        Console.WriteLine($"Respuesta vacía en el intento {attempt}");
                        if (attempt < 3)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en el intento {attempt} para el lote: {ex.Message}");
                        if (attempt == 3)
                        {
                            Console.WriteLine("Se alcanzó el máximo de intentos, omitiendo lote");
                            throw;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error procesando lote: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            }
        }

        Console.WriteLine($"Total de anuncios puntuados: {results.Count}");
        return results;
    }

    private async Task<string> SendOpenAIRequestAsync(string prompt)
    {
        try
        {
            Console.WriteLine("Adquiriendo semáforo para solicitud de API...");
            await _requestSemaphore.WaitAsync();

            try
            {
                var now = DateTime.UtcNow;
                var timeSinceLastRequest = now - _lastRequestTime;

                if (timeSinceLastRequest < _minRequestInterval)
                {
                    var waitTime = _minRequestInterval - timeSinceLastRequest;
                    Console.WriteLine($"Esperando {waitTime.TotalSeconds:F1} segundos antes de la próxima solicitud...");
                    await Task.Delay(waitTime);
                }

                Console.WriteLine("Preparando solicitud para la API de Grok...");
                if (string.IsNullOrEmpty(prompt))
                {
                    Console.WriteLine("Prompt vacío detectado");
                    return "Error: Prompt vacío";
                }

                var requestBody = new
                {
                    messages = new[]
                    {
                        new { role = "system", content = "Eres un analizador experto de anuncios." },
                        new { role = "user", content = prompt }
                    },
                    model = "grok-2-latest",
                    stream = false,
                    temperature = 0
                };

                var requestJson = JsonSerializer.Serialize(requestBody);
                var url = "https://api.x.ai/v1/chat/completions";

                _webClient.Headers.Clear();
                _webClient.Headers.Add("Authorization", $"Bearer {GROK_API_KEY}");
                _webClient.Headers.Add("Content-Type", "application/json");
                _webClient.Headers.Add("Accept", "application/json");

                Console.WriteLine("Enviando solicitud a la API de Grok...");
                var responseJson = await _webClient.UploadStringTaskAsync(url, "POST", requestJson);
                _lastRequestTime = DateTime.UtcNow;

                Console.WriteLine($"Respuesta cruda de la API recibida: {responseJson.Substring(0, Math.Min(100, responseJson.Length))}...");
                if (string.IsNullOrEmpty(responseJson))
                {
                    Console.WriteLine("Respuesta vacía de la API");
                    return "Error: Respuesta vacía de la API";
                }

                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseJson);

                if (responseObject.TryGetProperty("choices", out JsonElement choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement content))
                {
                    var contentString = content.GetString();
                    Console.WriteLine($"Contenido extraído: {contentString?.Substring(0, Math.Min(100, contentString?.Length ?? 0))}...");
                    return contentString ?? "Error: Contenido de respuesta inválido";
                }

                Console.WriteLine("Formato de respuesta inesperado");
                return "Error: Formato de respuesta inesperado";
            }
            finally
            {
                Console.WriteLine("Liberando semáforo");
                _requestSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en la API de Grok: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            return $"Error en la API de Grok: {ex.Message}";
        }
    }

    public List<(string Id, int Score, List<string> Positives, List<string> Negatives)> ParseAiResponse(string aiResponse)
    {
        var results = new List<(string, int, List<string>, List<string>)>();
        if (string.IsNullOrEmpty(aiResponse))
        {
            Console.WriteLine("Respuesta vacía de la IA recibida");
            return results;
        }

        try
        {
            Console.WriteLine("Parseando respuesta de la IA...");
            var responseObject = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(aiResponse);
            if (responseObject == null)
            {
                Console.WriteLine("No se pudo deserializar la respuesta de la IA");
                return results;
            }

            foreach (var item in responseObject)
            {
                try
                {
                    var id = item["Ad ID"]?.ToString();
                    var score = Convert.ToInt32(item["Score"]);
                    var positives = JsonSerializer.Deserialize<List<string>>(item["Positivos"].ToString()) ?? new List<string>();
                    var negatives = JsonSerializer.Deserialize<List<string>>(item["Negativos"].ToString()) ?? new List<string>();

                    if (!string.IsNullOrEmpty(id))
                    {
                        results.Add((id, score, positives, negatives));
                        Console.WriteLine($"Anuncio parseado: ID={id}, Puntuación={score}");
                    }
                    else
                    {
                        Console.WriteLine("ID de anuncio inválido en el elemento de respuesta");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parseando elemento de respuesta: {ex.Message}");
                }
            }

            Console.WriteLine($"Parseados correctamente {results.Count} anuncios de la respuesta de la IA");
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parseando respuesta de la IA: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            return results;
        }
    }

    public List<AdModel> GetBestAds(List<AdLight> ads, List<(string Id, int Score, List<string> Positives, List<string> Negatives)> scoredAds)
    {
        try
        {
            Console.WriteLine($"Filtrando los mejores anuncios de {scoredAds.Count} anuncios puntuados");
            var scoreDictionary = scoredAds.ToDictionary(ad => ad.Id, ad => (ad.Score, ad.Positives, ad.Negatives));

            foreach (var item in allAdsList)
            {
                if (scoreDictionary.TryGetValue(item.id, out var details))
                {
                    item.finalScore = details.Score;
                    item.goodThings = details.Positives;
                    item.badThings = details.Negatives;
                    Console.WriteLine($"Asignada puntuación {details.Score} al anuncio {item.id}");
                }
                else
                {
                    item.finalScore = 0;
                    item.goodThings = new List<string>();
                    item.badThings = new List<string>();
                    Console.WriteLine($"No se encontró puntuación para el anuncio {item.id}, asignada 0");
                }
            }

            var sortedAds = allAdsList.OrderByDescending(ad => ad.finalScore).ToList();
            Console.WriteLine($"Devolviendo {sortedAds.Count} anuncios ordenados");
            return sortedAds;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en GetBestAds: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            throw;
        }
    }

    public async Task<List<AdModel>> GetAds(SearchRequestDto request)
    {
        try
        {
            var results = new List<AdModel>();
            Console.WriteLine("Obteniendo anuncios para las plataformas...");

            if (request.PlatformIds.Contains(1))
            {
                try
                {
                    Console.WriteLine("Iniciando scraping de Wallapop...");
                    var wallapopResults = await FetchWallapop(request);
                    results.AddRange(wallapopResults);
                    Console.WriteLine($"Scraping de Wallapop completado con {wallapopResults.Count} resultados");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fallo en el scraping de Wallapop: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
                }
            }

            if (request.PlatformIds.Contains(2))
            {
                try
                {
                    Console.WriteLine("Iniciando scraping de Milanuncios...");
                    var milAnunciosResults = await FetchMilAnuncios(request);
                    results.AddRange(milAnunciosResults);
                    Console.WriteLine($"Scraping de Milanuncios completado con {milAnunciosResults.Count} resultados");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fallo en el scraping de Milanuncios: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
                }
            }

            Console.WriteLine($"Total de anuncios obtenidos: {results.Count}");
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en GetAds: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            throw;
        }
    }

    public async Task<List<AdModel>> FetchWallapop(SearchRequestDto request)
    {
        try
        {
            Console.WriteLine($"Obteniendo anuncios de Wallapop para la categoría {request.Category}");
            var categoryMapping = await _context.PlatformCategoryMappings
                .Where(pcm => pcm.PlatformId == 1 && pcm.CategoryId == request.Category && pcm.IsActive)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (categoryMapping == null)
            {
                Console.WriteLine($"No se encontró un mapeo de categoría activo para la categoría {request.Category} en Wallapop");
                return new List<AdModel>();
            }
            Console.WriteLine($"Mapeo de categoría encontrado: {categoryMapping.UrlParameter}");

            Console.WriteLine("Llamando a Web3Data.SearchWallapop...");
            string jsonResponse = await _web3Data.SearchWallapop(
                request.Keywords,
                request.PagesToScrape,
                int.Parse(categoryMapping.UrlParameter),
                request.Latitude,
                request.Longitude,
                request.MinPrice ?? 0,
                request.MaxPrice ?? int.MaxValue,
                request.ShippingAvailable,
                request.IsProgrammed);

            Console.WriteLine($"Respuesta de Wallapop recibida: {jsonResponse.Substring(0, Math.Min(100, jsonResponse.Length))}...");
            if (string.IsNullOrEmpty(jsonResponse))
            {
                Console.WriteLine("Respuesta vacía de Wallapop");
                return new List<AdModel>();
            }

            var ads = JsonSerializer.Deserialize<List<AdModel>>(jsonResponse) ?? new List<AdModel>();
            Console.WriteLine($"Deserializados {ads.Count} anuncios de Wallapop");
            return ads;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en FetchWallapop: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            throw;
        }
    }

    public async Task<List<AdModel>> FetchMilAnuncios(SearchRequestDto request)
    {
        try
        {
            Console.WriteLine($"Obteniendo anuncios de Milanuncios para la categoría {request.Category}");
            var categoryMapping = await _context.PlatformCategoryMappings
                .Where(pcm => pcm.PlatformId == 2 && pcm.CategoryId == request.Category && pcm.IsActive)
                .FirstOrDefaultAsync();

            if (categoryMapping == null)
            {
                Console.WriteLine($"No se encontró un mapeo de categoría activo para la categoría {request.Category} en Milanuncios");
                return new List<AdModel>();
            }
            Console.WriteLine($"Mapeo de categoría encontrado: {categoryMapping.UrlParameter}");

            // Obtener la URL según el entorno

            var url = "http://milanuncios-scrapper-py-svc:7000/ads";

            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("Error: No se encontró la URL del servicio de Milanuncios en la configuración.");
                throw new InvalidOperationException("No se encontró la URL del servicio de Milanuncios.");
            }
            Console.WriteLine($"Usando URL de Milanuncios: {url}");

            var requestBody = new
            {
                searchTerms = request.Keywords,
                category = categoryMapping.UrlParameter,
                latitude = request.Latitude,
                longitude = request.Longitude,
                minPrice = request.MinPrice,
                maxPrice = request.MaxPrice,
                shippingAviable = request.ShippingAvailable,
                isMultipage = request.IsMultiPage,
                pagesorpage = request.PagesToScrape,
                isProgrammed = request.IsProgrammed
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            Console.WriteLine($"Enviando solicitud a la API de Milanuncios: {jsonContent}");

            _webClient.Headers.Clear();
            _webClient.Headers.Add("Content-Type", "application/json");

            Console.WriteLine("Enviando solicitud a la API de Milanuncios...");
            string responseJson = await _webClient.UploadStringTaskAsync(url, "POST", jsonContent);
            Console.WriteLine($"Respuesta de Milanuncios recibida: {responseJson.Substring(0, Math.Min(100, responseJson.Length))}...");

            var adsList = JsonSerializer.Deserialize<List<DataLayer.Models.MilAnuncios.Root>>(responseJson);
            if (adsList == null)
            {
                Console.WriteLine("No se pudo deserializar la respuesta de Milanuncios");
                return new List<AdModel>();
            }

            var listadomapeado = _mapper.Map<List<AdModel>>(adsList);
            Console.WriteLine($"Mapeados {listadomapeado.Count} anuncios de Milanuncios");
            return listadomapeado;
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"La solicitud de Milanuncios ha expirado: {ex.Message}");
            return new List<AdModel>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en FetchMilAnuncios: {ex.Message}\nSeguimiento de pila: {ex.StackTrace}");
            throw;
        }
    }

    //ANALISIS OFFLINE DE ANUNCIOS
    public class AdAnalysis
    {
        public double AveragePrice { get; set; }
        public double MedianPrice { get; set; }
        public double PriceStandardDeviation { get; set; }
        public Dictionary<string, int> KeywordFrequency { get; set; }
        public List<AdLight> PotentialDeals { get; set; }
        public Dictionary<string, double> PricePercentiles { get; set; }
        public List<AdLight> OutlierDeals { get; set; }
        public Dictionary<string, List<AdLight>> PriceBrackets { get; set; }
    }
}