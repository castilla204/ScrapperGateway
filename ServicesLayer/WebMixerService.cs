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
        _webClient = new WebClient(); // Inicializa _webClient aquí

        potentialDeals = new();
    }


    //funcion busqueda general
    public async Task<List<AdModel>> Search(
      SearchRequestDto request)
    {
        try
        {
            categoryString = _context.Categories.FirstOrDefaultAsync(z => z.Id == request.Category).Result.Name;

            // Obtener todos los anuncios.
            allAdsList = await GetAds(request);
            List<AdModel> listaAnuncios = [];

            var adsLight = MapAdsToLightFormat(allAdsList);


            // Dividir los anuncios en lotes para enviarlos a la IA.
            var batches = SplitAdsIntoBatches(adsLight);

            // Obtener las puntuaciones de los anuncios desde la IA.
            var scores = await GetAdScoresFromAI(batches, request.UserSearch, request.Keywords, request.Category, request.IsProgrammed);

            // Filtrar los mejores anuncios según las puntuaciones.
            listaAnuncios = GetBestAds(adsLight, scores);

            List<Ad> listadomapeado = _mapper.Map<List<Ad>>(listaAnuncios);

            // Filtrar anuncios únicos antes de agregarlos
            var uniqueAds = listadomapeado
                .GroupBy(ad => ad.Id)
                .Select(g => g.First()) // Mantener solo el primer anuncio para cada Id
                .ToList();

            // Obtener IDs de los anuncios que ya existen en la base de datos
            var existingAdIds = await _context.Ads
                .Where(ad => uniqueAds.Select(l => l.Id).Contains(ad.Id))
                .Where(ad => !string.IsNullOrEmpty(ad.Slug))
                .Select(ad => ad.Id)
                .ToListAsync();

            // Filtrar los anuncios nuevos que no están en la base de datos
            var newAds = uniqueAds.Where(ad => !existingAdIds.Contains(ad.Id)).ToList();

            // Agregar solo los anuncios nuevos a la base de datos
            await _context.Ads.AddRangeAsync(newAds);
            await _context.SaveChangesAsync();

            //}
            //else
            //{
            //    listaAnuncios = allAdsList;
            //}

            // Return the list of analyzed ads
            return listaAnuncios;
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }

    public List<AdLight> MapAdsToLightFormat(List<AdModel> allAdsList)
    {
        const int MAX_DESCRIPTION_LENGTH = 500;

        return allAdsList.Select(ad => new AdLight
        {
            Id = ad.id,
            Title = ad.title,
            Description = ad.description?.Length > MAX_DESCRIPTION_LENGTH
                ? ad.description.Substring(0, MAX_DESCRIPTION_LENGTH) + "..."
                : ad.description ?? string.Empty,
            Price = ad.price
        }).ToList();
    }

    public List<List<AdLight>> SplitAdsIntoBatches(List<AdLight> ads, int batchSize = 15)
    {
        return ads.Select((ad, index) => new { ad, index })
                  .GroupBy(x => x.index / batchSize)
                  .Select(group => group.Select(x => x.ad).ToList())
                  .ToList();
    }

    public string GeneratePrompt(List<AdLight> adsBatch, string userSearch, string keywords, int? category, bool isProgrammed)
    {
        var adsString = string.Join("\n", adsBatch.Select(ad =>
            $"ID: {ad.Id}\nTítulo: {ad.Title}\nDescripción: {ad.Description}\nPrecio: {ad.Price}€\n"));

        return $@"
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

        Si ningún anuncio coincide, responde con:
        []

        ";
    }

    public async Task<List<(string Id, int Score, List<string> Positives, List<string> Negatives)>> GetAdScoresFromAI(
     List<List<AdLight>> adBatches, string userSearch, string keywords, int? category, bool isProgrammed)
    {
        var results = new List<(string Id, int Score, List<string> Positives, List<string> Negatives)>();

        foreach (var batch in adBatches)
        {
            try
            {
                // Generar el prompt para este batch
                var prompt = GeneratePrompt(batch, userSearch, keywords, category, isProgrammed);

                // Esperar antes de hacer la siguiente solicitud
                await Task.Delay(TimeSpan.FromSeconds(2));

                // Intentar hasta 3 veces
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var response = await SendOpenAIRequestAsync(prompt);
                        if (!string.IsNullOrEmpty(response))
                        {
                            results.AddRange(ParseAiResponse(response));
                            break;
                        }

                        if (attempt < 3)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en intento {attempt} para batch: {ex.Message}");
                        if (attempt == 3) throw;
                        await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error procesando batch de anuncios: {ex.Message}");
            }
        }

        return results;
    }

    private async Task<string> SendOpenAIRequestAsync(string prompt)
    {
        try
        {
            // Acquire semaphore to ensure only one request at a time
            await _requestSemaphore.WaitAsync();

            try
            {
                var now = DateTime.UtcNow;
                var timeSinceLastRequest = now - _lastRequestTime;

                // If not enough time has passed since the last request, wait
                if (timeSinceLastRequest < _minRequestInterval)
                {
                    var waitTime = _minRequestInterval - timeSinceLastRequest;
                    Console.WriteLine($"Waiting {waitTime.TotalSeconds:F1} seconds before next request...");
                    await Task.Delay(waitTime);
                }

                Console.WriteLine("Sending request to Grok API...");

                if (string.IsNullOrEmpty(prompt))
                {
                    Console.WriteLine("Empty prompt received.");
                    return "Error: Empty prompt";
                }

                var requestBody = new
                {
                    messages = new[]
                    {
                        new { role = "system", content = "You are a expert ad analyzer ." },
                        new { role = "user", content = prompt }
                    },
                    model = "grok-2-latest",
                    stream = false,
                    temperature = 0
                };

                var requestJson = JsonSerializer.Serialize(requestBody);
                var url = "https://api.x.ai/v1/chat/completions";

                // Set up headers
                _webClient.Headers.Clear();
                _webClient.Headers.Add("Authorization", $"Bearer {GROK_API_KEY}");
                _webClient.Headers.Add("Content-Type", "application/json");
                _webClient.Headers.Add("Accept", "application/json");

                // Make the request
                var responseJson = await _webClient.UploadStringTaskAsync(url, "POST", requestJson);

                // Update last request time AFTER the request is made
                _lastRequestTime = DateTime.UtcNow;

                Console.WriteLine($"Raw API Response: {responseJson}");

                if (string.IsNullOrEmpty(responseJson))
                {
                    Console.WriteLine("Empty response from API.");
                    return "Error: Empty response from API";
                }

                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseJson);

                if (responseObject.TryGetProperty("choices", out JsonElement choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement content))
                {
                    return content.GetString() ?? "Error: Invalid response content";
                }

                Console.WriteLine("Unexpected response format");
                return "Error: Unexpected response format";
            }
            finally
            {
                _requestSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Grok API error: {ex.Message}");
            return $"Grok API error: {ex.Message}";
        }
    }

    public List<(string Id, int Score, List<string> Positives, List<string> Negatives)> ParseAiResponse(string aiResponse)
    {
        var results = new List<(string, int, List<string>, List<string>)>();
        if (string.IsNullOrEmpty(aiResponse))
        {
            Console.WriteLine("Empty AI response received");
            return results;
        }

        var lines = aiResponse.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string id = "";
        int score = 0;
        List<string> positives = new List<string>();
        List<string> negatives = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains("Ad ID:") && line.Contains("Score:"))
            {
                try
                {
                    var parts = line.Split('-');
                    id = parts[0].Split(':')[1].Trim();
                    score = int.Parse(parts[1].Split(':')[1].Trim());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing line: {line}. Error: {ex.Message}");
                    continue;
                }
            }
            else if (line.StartsWith("Positivos:"))
            {
                positives = line.Substring(line.IndexOf(':') + 1)
                    .Split(',')
                    .Select(p => p.Trim())
                    .ToList();
            }
            else if (line.StartsWith("Negativos:"))
            {
                negatives = line.Substring(line.IndexOf(':') + 1)
                    .Split(',')
                    .Select(n => n.Trim())
                    .ToList();

                if (!string.IsNullOrEmpty(id))
                {
                    results.Add((id, score, positives, negatives));
                    id = ""; score = 0;
                    positives = new List<string>();
                    negatives = new List<string>();
                }
            }
        }
        return results;
    }

    public List<AdModel> GetBestAds(List<AdLight> ads, List<(string Id, int Score, List<string> Positives, List<string> Negatives)> scoredAds)
    {
        // Crear un diccionario único que mapea el ID con la tupla completa de score, positivos y negativos.
        var scoreDictionary = scoredAds.ToDictionary(ad => ad.Id, ad => (ad.Score, ad.Positives, ad.Negatives));

        // Asignar las puntuaciones finales y detalles a los anuncios en un solo bucle
        foreach (var item in allAdsList)
        {
            if (scoreDictionary.TryGetValue(item.id, out var details))
            {
                item.finalScore = details.Score;
                item.goodThings = details.Positives;
                item.badThings = details.Negatives;
            }
            else
            {
                item.finalScore = 0;
                item.goodThings = new List<string>();
                item.badThings = new List<string>();
            }
        }

        // Ordenar la lista por finalScore en orden descendente
        return allAdsList
            .OrderByDescending(ad => ad.finalScore)
            .ToList();
    }

    public async Task<List<AdModel>> GetAds(SearchRequestDto request)
    {
        try
        {
            var results = new List<AdModel>();

            if (request.PlatformIds.Contains(1))
            {
                var wallapopResults = await FetchWallapop(request);
                results.AddRange(wallapopResults);
            }

            if (request.PlatformIds.Contains(2))
            {
                var milAnunciosResults = await FetchMilAnuncios(request);
                results.AddRange(milAnunciosResults);
            }

            return results;
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }


    public async Task<List<AdModel>> FetchWallapop(SearchRequestDto request)
    {
        // Get URL parameter from database


        var categoryMapping = await _context.PlatformCategoryMappings
            .Where(pcm => pcm.PlatformId == 1 &&
                         pcm.CategoryId == request.Category &&
                         pcm.IsActive)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (categoryMapping == null)
        {

            return new List<AdModel>();
        }


        string jsonResponse = "";

        jsonResponse = await _web3Data.SearchWallapop(
            request.Keywords,
            request.PagesToScrape,
            int.Parse(categoryMapping.UrlParameter),
            request.Latitude,
            request.Longitude,
            request.MinPrice ?? 0,
            request.MaxPrice ?? int.MaxValue,
            request.ShippingAvailable,
            request.IsProgrammed);




        return JsonSerializer.Deserialize<List<AdModel>>(jsonResponse) ?? new List<AdModel>();
    }

    public async Task<List<AdModel>> FetchMilAnuncios(SearchRequestDto request)
    {
        // Get URL parameter from database
        var categoryMapping = await _context.PlatformCategoryMappings
            .Where(pcm => pcm.PlatformId == 2 && pcm.CategoryId == request.Category)
            .Where(pcm => pcm.IsActive)
            .FirstOrDefaultAsync();

        if (categoryMapping == null)
        {
            return new List<AdModel>();
        }

        string categoryString = categoryMapping.UrlParameter;

        var url = "http://localhost:7000/ads";

        // Crear el cuerpo de la solicitud JSON
        var requestBody = new
        {
            searchTerms = request.Keywords,
            category = categoryString,
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

        Console.WriteLine("Realizando solicitud a la API Web4...");

        try
        {
            _webClient.Headers.Clear();
            _webClient.Headers.Add("Content-Type", "application/json");
            string responseJson = await _webClient.UploadStringTaskAsync(url, "POST", jsonContent);

            try
            {
                var adsList = JsonSerializer.Deserialize<List<DataLayer.Models.MilAnuncios.Root>>(responseJson);
                List<AdModel> listadomapeado = _mapper.Map<List<AdModel>>(adsList);
                return listadomapeado;
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("La solicitud ha tardado demasiado y ha sido cancelada.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }

        return new List<AdModel>();
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