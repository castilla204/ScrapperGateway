using AutoMapper;
using DataLayer;
using Microsoft.Extensions.Logging;

namespace ServicesLayer
{
    public class Web3Service : IWeb3Service
    {
        private readonly IWeb3Data _web3Data;
        private readonly IMapper _mapper;
        private readonly ILogger<Web3Service> _logger;

        public Web3Service(IWeb3Data web3Data, IMapper mapper, ILogger<Web3Service> logger)
        {
            _web3Data = web3Data;
            _mapper = mapper;
            _logger = logger;
            _logger.LogInformation("Web3Service inicializado correctamente.");
        }

        public async Task<string> GetWallapop(string keywords, int pagestoscrap, int? category, string? latitude, string? longitude, int? minprice, int? maxprice, bool shippingAviable, bool isProgrammed)
        {
            _logger.LogInformation("Llamando a Web3Data.SearchWallapop con palabras clave: {Keywords}", keywords);
            return await _web3Data.SearchWallapop(keywords, pagestoscrap, category, latitude, longitude, minprice, maxprice, shippingAviable, isProgrammed);
        }
    }
}