using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OficinaOs.Infrastructure.Messaging;

/// <summary>Long polling da fila oficina-{env}-os-queue. Cada mensagem é processada em sequência; só é apagada se der certo.</summary>
public class SqsConsumer : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly SqsMessageProcessor _processor;
    private readonly MessagingOptions _options;
    private readonly ILogger<SqsConsumer> _logger;

    public SqsConsumer(IAmazonSQS sqs, SqsMessageProcessor processor, IOptions<MessagingOptions> options, ILogger<SqsConsumer> logger)
    {
        _sqs = sqs;
        _processor = processor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Messaging:Enabled=false — SqsConsumer desligado; nenhum evento externo será consumido.");
            return;
        }

        var queueUrl = await ResolveQueueUrlAsync(stoppingToken);
        if (queueUrl is null) return;
        _logger.LogInformation("SqsConsumer iniciado na fila {QueueUrl}", queueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = Math.Clamp(_options.MaxMessages, 1, 10),
                    WaitTimeSeconds = Math.Clamp(_options.WaitTimeSeconds, 0, 20)
                }, stoppingToken);

                foreach (var message in response.Messages ?? new List<Message>())
                {
                    try
                    {
                        var result = await _processor.ProcessAsync(message.Body, stoppingToken);
                        await _sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                        _logger.LogDebug("Mensagem {MessageId} processada: {Result}", message.MessageId, result);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Não apaga: volta à fila; depois de 3 recebimentos a redrive policy manda para a DLQ.
                        _logger.LogError(ex, "Falha ao processar a mensagem {MessageId}; ela voltará à fila (DLQ após 3 tentativas)", message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao ler da fila; nova tentativa em 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<string?> ResolveQueueUrlAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.QueueUrl)) return _options.QueueUrl;

        var name = string.IsNullOrWhiteSpace(_options.QueueName) ? $"oficina-{_options.Environment}-os-queue" : _options.QueueName;
        // A fila é criada pelo Terraform/init; se ainda não existe, tenta de novo em vez de derrubar o serviço.
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                return (await _sqs.GetQueueUrlAsync(name, ct)).QueueUrl;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Fila {Queue} indisponível (tentativa {Attempt}): {Message}", name, attempt, ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 3)), ct); }
                catch (OperationCanceledException) { return null; }
            }
        }
        return null;
    }
}
