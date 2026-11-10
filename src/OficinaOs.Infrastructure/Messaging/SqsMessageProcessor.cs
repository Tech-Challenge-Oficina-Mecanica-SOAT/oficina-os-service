using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OficinaOs.Application.Events;

namespace OficinaOs.Infrastructure.Messaging;

/// <summary>
/// Processa UMA mensagem da fila: lê o envelope, retoma o trace (correlationId) e chama o handler da Saga.
/// Exceção = a mensagem NÃO é apagada da fila (volta após o visibility timeout e, depois de 3 recebimentos, vai para a DLQ).
/// </summary>
public class SqsMessageProcessor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SqsMessageProcessor> _logger;

    public SqsMessageProcessor(IServiceScopeFactory scopes, ILogger<SqsMessageProcessor> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<HandleResult> ProcessAsync(string body, CancellationToken ct = default)
    {
        var evt = IncomingEventParser.Parse(body);

        // Continua o mesmo trace do evento recebido: o os.cancelada gerado agora sai com o MESMO correlationId.
        using var activity = new Activity($"consume {evt.EventType}");
        if (!string.IsNullOrWhiteSpace(evt.CorrelationId)) activity.SetParentId(evt.CorrelationId);
        activity.Start();

        using var scope = _scopes.CreateScope();
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = evt.EventId, ["EventType"] = evt.EventType, ["CorrelationId"] = evt.CorrelationId
        });

        var handler = scope.ServiceProvider.GetRequiredService<IncomingEventHandler>();
        return await handler.HandleAsync(evt, ct);
    }
}
