using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using ScrapperGateway.Models.Wallapop;
using AutoMapper;
using DataLayer.Models;
using System.Net;

namespace DataLayer
{
    public class Web3Data : IWeb3Data
    {
        private readonly HttpClient client = new();
        private readonly string deviceId;
        private readonly string mpid;
        private const string APP_VERSION = "83070";
        private readonly IMapper _mapper;
        private readonly ILogger<Web3Data> _logger;
        private string categoryString;

        public Web3Data(IMapper mapper, ILogger<Web3Data> logger)
        {
            _mapper = mapper;
            _logger = logger;
            deviceId = Guid.NewGuid().ToString();
            mpid = GenerateMPID();
            client = new HttpClient();
            SetupHttpClient();
            _logger.LogInformation("Web3Data inicializado con DeviceID: {DeviceId}, MPID: {MPID}", deviceId, mpid);
        }

        private HttpClient CreateHttpClientWithProxy()
        {
            _logger.LogInformation("Creando HttpClient con configuración de proxy");
            var webProxy = new WebProxy("brd.superproxy.io:33335");
            var proxyCredentials = new NetworkCredential(
                "brd-customer-hl_116e4d7f-zone-datacenter_proxy1-country-es",
                "rors9j8p0x3c"
            );
            webProxy.Credentials = proxyCredentials;

            var handler = new HttpClientHandler
            {
                Proxy = webProxy,
                UseProxy = true,
                PreAuthenticate = false,
                UseDefaultCredentials = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler);
            _logger.LogInformation("HttpClient con proxy creado exitosamente");
            return client;
        }

        private string GenerateMPID()
        {
            Random random = new Random();
            var generatedMPID = (8000000000000000000 + random.Next(1999999999)).ToString();
            _logger.LogDebug("MPID generado: {MPID}", generatedMPID);
            return generatedMPID;
        }

        private void SetupHttpClient()
        {
            _logger.LogDebug("Configurando cabeceras de HttpClient");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "es,es-ES;q=0.9");
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.Add("DNT", "1");
            client.DefaultRequestHeaders.Add("DeviceOS", "0");
            client.DefaultRequestHeaders.Add("MPID", mpid);
            client.DefaultRequestHeaders.Add("Origin", "https://es.wallapop.com");
            client.DefaultRequestHeaders.Add("Pragma", "no-cache");
            client.DefaultRequestHeaders.Add("Referer", "https://es.wallapop.com/");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-site");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-AppVersion", APP_VERSION);
            client.DefaultRequestHeaders.Add("X-DeviceID", deviceId);
            client.DefaultRequestHeaders.Add("X-DeviceOS", "0");
            client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"129\", \"Not=A?Brand\";v=\"8\", \"Chromium\";v=\"129\"");
            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            client.Timeout = TimeSpan.FromSeconds(30);
            _logger.LogDebug("Cabeceras de HttpClient configuradas exitosamente");
        }

        public async Task<string> SearchWallapop(string keywords, int pagestoscrap, int? category, string? latitude, string? longitude, int? minprice, int? maxprice, bool shippingAviable, bool isProgrammed)
        {
            _logger.LogInformation("Iniciando búsqueda en Wallapop con palabras clave: {Keywords}, páginas: {PagesToScrap}, categoría: {Category}", keywords, pagestoscrap, category);

            latitude = "41.76401";
            longitude = "-2.46883";
            keywords = keywords ?? "quad";
            minprice = minprice ?? 1000;
            maxprice = maxprice ?? 2000;
            category = category ?? 0;
            string shipping = shippingAviable ? "true" : "";
            isProgrammed = false;

            List<AdModel> anuncios = new List<AdModel>();

            try
            {
                _logger.LogDebug("Enviando solicitud inicial a https://es.wallapop.com");
                await client.GetAsync("https://es.wallapop.com");
                await Task.Delay(TimeSpan.FromSeconds(2));

                for (int page = 0; page < pagestoscrap; page++)
                {
                    _logger.LogInformation("Procesando página {Page} de {TotalPages}", page + 1, pagestoscrap);
                    if (category != 0)
                    {
                        categoryString = $"category_ids={category}";
                    }
                    var start = page * 40;
                    var apiUrl = $"https://api.wallapop.com/api/v3/general/search?{categoryString}&keywords={keywords}" +
                        $"&filters_source=search_box" +
                        $"&latitude={latitude}" +
                        $"&longitude={longitude}" +
                        $"&min_sale_price={minprice}" +
                        $"&max_sale_price={maxprice}" +
                        $"&start={start}" +
                        $"&show_multiple_sections=false" +
                        $"&is_shippable={(shippingAviable ? "true" : "false")}" +
                        (isProgrammed ? $"&time_filter=today" : string.Empty);

                    _logger.LogDebug("Enviando solicitud a la URL de la API: {ApiUrl}", apiUrl);
                    HttpResponseMessage response;
                    try
                    {
                        response = await client.GetAsync(apiUrl);
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError(ex, "Error de red al realizar la solicitud HTTP a la página {Page}: {ErrorMessage}", page + 1, ex.Message);
                        continue;
                    }
                    catch (TaskCanceledException ex)
                    {
                        _logger.LogError(ex, "Tiempo de espera agotado al realizar la solicitud a la página {Page}: {ErrorMessage}", page + 1, ex.Message);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Solicitud fallida para la página {Page}. Código de estado: {StatusCode}, Razón: {ReasonPhrase}",
                            page + 1, response.StatusCode, response.ReasonPhrase);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("Respuesta recibida para la página {Page}", page + 1);
                    JObject data;
                    try
                    {
                        data = JObject.Parse(json);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Error al parsear JSON en la página {Page}: {ErrorMessage}", page + 1, ex.Message);
                        continue;
                    }

                    List<ScrapperGateway.Models.Wallapop.Root> pageAnuncios;
                    try
                    {
                        pageAnuncios = JsonConvert.DeserializeObject<List<ScrapperGateway.Models.Wallapop.Root>>(data["search_objects"].ToString());
                        _logger.LogInformation("Deserializados {Count} elementos para la página {Page}", pageAnuncios?.Count ?? 0, page + 1);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Error al deserializar objetos en la página {Page}: {ErrorMessage}", page + 1, ex.Message);
                        continue;
                    }

                    var mappedAnuncios = _mapper.Map<List<AdModel>>(pageAnuncios);
                    anuncios.AddRange(mappedAnuncios);
                    _logger.LogInformation("Mapeados y añadidos {Count} elementos para la página {Page}. Total de elementos: {TotalCount}",
                        mappedAnuncios.Count, page + 1, anuncios.Count);

                    if (page < pagestoscrap - 1)
                    {
                        _logger.LogDebug("Esperando 1 segundo antes de la siguiente página");
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }

                _logger.LogInformation("Búsqueda completada. Total de elementos recolectados: {TotalCount}", anuncios.Count);
                return JsonConvert.SerializeObject(anuncios);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general durante la búsqueda en Wallapop: Tipo: {ExceptionType}, Mensaje: {ErrorMessage}",
                    ex.GetType().Name, ex.Message);
                return JsonConvert.SerializeObject(new List<AdModel>());
            }
        }
    }
}