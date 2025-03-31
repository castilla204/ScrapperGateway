using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;

namespace ScrapperGateway.Services
{
    public class RabbitMQService : IRabbitMQService, IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;

        public RabbitMQService(IConnectionFactory connectionFactory)
        {
            _connection = connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();
        }

        public void PublishMessage<T>(string queueName, T message)
        {
            _channel.QueueDeclare(queue: queueName,
                                durable: false,
                                exclusive: false,
                                autoDelete: false,
                                arguments: null);

            var json = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(json);

            _channel.BasicPublish(exchange: "",
                                routingKey: queueName,
                                basicProperties: null,
                                body: body);
        }

        public async Task<T> SendAndReceiveAsync<T>(string requestQueueName, string replyQueueName, object message, int timeout = 30000)
        {
            var correlationId = Guid.NewGuid().ToString();
            var replyEvent = new TaskCompletionSource<T>();

            _channel.QueueDeclare(queue: requestQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueDeclare(queue: replyQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

            var consumer = new EventingBasicConsumer(_channel);
            string consumerTag = _channel.BasicConsume(
                queue: replyQueueName,
                autoAck: true,
                consumer: consumer);

            consumer.Received += (model, ea) =>
            {
                if (ea.BasicProperties.CorrelationId == correlationId)
                {
                    var body = ea.Body.ToArray();
                    var response = Encoding.UTF8.GetString(body);
                    var result = JsonConvert.DeserializeObject<T>(response);
                    replyEvent.TrySetResult(result);
                }
            };

            try
            {
                var props = _channel.CreateBasicProperties();
                props.CorrelationId = correlationId;
                props.ReplyTo = replyQueueName;

                var json = JsonConvert.SerializeObject(message);
                var body = Encoding.UTF8.GetBytes(json);

                _channel.BasicPublish(
                    exchange: "",
                    routingKey: requestQueueName,
                    basicProperties: props,
                    body: body);

                if (await Task.WhenAny(replyEvent.Task, Task.Delay(timeout)) != replyEvent.Task)
                {
                    throw new TimeoutException("The request timed out.");
                }

                return await replyEvent.Task;
            }
            finally
            {
                _channel.BasicCancel(consumerTag);
            }
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}