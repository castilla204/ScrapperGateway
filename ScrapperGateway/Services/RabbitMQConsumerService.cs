using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;
using ServicesLayer;
using DataLayer.Models.DTOs;
using DataLayer.Models;
using DataLayer.Models.PostGresModels;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ScrapperGateway.Services
{
    public class RabbitMQConsumerService : IHostedService
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RabbitMQConsumerService> _logger;
        private readonly ConcurrentDictionary<string, DateTime> _processedMessages = new();

        public RabbitMQConsumerService(
            IConnectionFactory connectionFactory,
            IServiceScopeFactory scopeFactory,
            ILogger<RabbitMQConsumerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            try
            {
                _connection = connectionFactory.CreateConnection();
                _channel = _connection.CreateModel();

                // Configurar la cola con parámetros específicos para evitar duplicados
                var arguments = new Dictionary<string, object>
                {
                    { "x-message-ttl", 300000 }, // 5 minutos - mantener consistente con SearchDaemon
                    { "x-max-length", 100000 },
                    { "x-overflow", "reject-publish" }
                };
                _channel.QueueDeclare(
                    queue: "scrapper_request_queue",
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: arguments);


                // Configurar QoS para asegurar procesamiento secuencial
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing RabbitMQ consumer");
                throw;
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var request = JsonConvert.DeserializeObject<SearchRequestDto>(message);

                    if (request == null)
                    {
                        _logger.LogError("Failed to deserialize search request");
                        _channel.BasicReject(ea.DeliveryTag, false);
                        return;
                    }

                    string messageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();

                    // Verificar si el mensaje ya fue procesado
                    if (_processedMessages.TryGetValue(messageId, out var lastProcessed))
                    {
                        _logger.LogWarning($"Mensaje duplicado detectado, ID: {messageId}, último procesamiento: {lastProcessed}");
                        _channel.BasicAck(ea.DeliveryTag, false);
                        return;
                    }

                    try
                    {
                        _logger.LogInformation($"Procesando mensaje nuevo {messageId}");
                        _processedMessages.TryAdd(messageId, DateTime.UtcNow);

                        // Confirmar recepción inmediatamente para eliminar de la cola
                        _channel.BasicAck(ea.DeliveryTag, false);

                        using var scope = _scopeFactory.CreateScope();
                        var webMixerService = scope.ServiceProvider.GetRequiredService<IWebMixerService>();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var result = await webMixerService.Search(request);

                        if (request.IsProgrammed)
                        {
                            foreach (var ad in result)
                            {
                                var dbAd = await dbContext.Ads.FindAsync(ad.id);
                                if (dbAd == null)
                                {
                                    dbAd = new Ad
                                    {
                                        Id = ad.id,
                                        Title = ad.title,
                                        Description = ad.description,
                                        Url = ad.url,
                                        Price = (decimal?)ad.price,
                                        Images = ad.images?.ToArray(),
                                        AdScore = ad.Adscore,
                                        FinalScore = ad.finalScore,
                                        GoodThings = ad.goodThings?.ToArray(),
                                        BadThings = ad.badThings?.ToArray(),
                                        PublishDate = ad.publishDate.ToUniversalTime(),
                                        Category = ad.category,
                                        CategoryId = ad.categoryId,
                                        Province = ad.province,
                                        ProvinceId = ad.provinceId,
                                        City = ad.city,
                                        CityId = ad.cityId,
                                        Highlighted = ad.highlighted,
                                        IsNew = ad.isNew,
                                        IsReserved = ad.isReserved == "true",
                                        Slug = ad.slug,
                                        SellerType = ad.sellerType,
                                        Tags = ad.tags?.ToArray() ?? Array.Empty<string>(),
                                        UpdateDate = ad.updateDate.ToUniversalTime(),
                                        ScrappedDate = ad.ScrappedDate.ToUniversalTime(),
                                        PlatformId = ad.PlatformId
                                    };
                                    dbContext.Ads.Add(dbAd);
                                }

                                var existingSearchResult = await dbContext.SearchResults
                                    .AnyAsync(sr => sr.SearchId == request.SearchId && sr.AdId == ad.id);

                                if (!existingSearchResult)
                                {
                                    var searchResult = new SearchResult
                                    {
                                        SearchId = request.SearchId,
                                        AdId = ad.id,
                                        FoundAt = DateTime.UtcNow
                                    };
                                    dbContext.SearchResults.Add(searchResult);
                                }
                            }

                            await dbContext.SaveChangesAsync();
                        }


                        _logger.LogInformation($"Mensaje {messageId} procesado exitosamente");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error procesando mensaje {messageId}");
                        // No reencolar el mensaje en caso de error
                    }
                    finally
                    {
                        // Limpiar mensajes procesados después de un tiempo
                        _ = Task.Delay(TimeSpan.FromHours(1))
                            .ContinueWith(_ =>
                            {
                                DateTime lastProcessTime;
                                _processedMessages.TryRemove(messageId, out lastProcessTime);
                            });
                    }
                };

                _channel.BasicConsume(
                    queue: "scrapper_request_queue",
                    autoAck: false,
                    consumer: consumer);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting RabbitMQ consumer");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _channel?.Close();
                _channel?.Dispose();
                _connection?.Close();
                _connection?.Dispose();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping RabbitMQ consumer");
                throw;
            }
        }
    }
}