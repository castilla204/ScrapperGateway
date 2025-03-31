using DataLayer.Models;

namespace ScrapperGateway.Services
{
    public class ScrapperService : IScrapperService
    {
        private readonly IRabbitMQService _rabbitMQService;

        public ScrapperService(IRabbitMQService rabbitMQService)
        {
            _rabbitMQService = rabbitMQService;
        }

        public async Task<List<AdModel>> SearchAsync(string keywords, string userSearch, int pagestoscrape,
            int? category, string? latitude, string? longitude, int? minprice, int? maxprice,
            int? brandId, int? modelId, bool analyze, bool isMultiPage, bool shippingAviable)
        {
            var searchRequest = new
            {
                Keywords = keywords,
                UserSearch = userSearch,
                PagesToScrape = pagestoscrape,
                Category = category,
                Latitude = latitude,
                Longitude = longitude,
                MinPrice = minprice,
                MaxPrice = maxprice,
                BrandId = brandId,
                ModelId = modelId,
                Analyze = analyze,
                IsMultiPage = isMultiPage,
                ShippingAviable = shippingAviable
            };

            var result = await _rabbitMQService.SendAndReceiveAsync<List<AdModel>>(
                "scrapper_request_queue",
                "scrapper_response_queue",
                searchRequest
            );

            return result;
        }
    }
}