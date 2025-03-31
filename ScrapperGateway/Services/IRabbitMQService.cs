using System;
using System.Threading.Tasks;

namespace ScrapperGateway.Services
{
    public interface IRabbitMQService : IDisposable
    {
        void PublishMessage<T>(string queueName, T message);
        Task<T> SendAndReceiveAsync<T>(string requestQueueName, string replyQueueName, object message, int timeout = 30000);
    }
}