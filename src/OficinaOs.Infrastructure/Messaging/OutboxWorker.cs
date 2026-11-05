using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OficinaOs.Infrastructure.Messaging;

/// <summary>Serviço em background que esvazia a outbox periodicamente.</summary>
public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly MessagingOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(IServiceScopeFactory scopes, IOptions<MessagingOptions> options, ILogger<OutboxWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Messaging:Enabled=false — OutboxWorker desligado; eventos ficam pendentes em outbox_events.");
            return;
        }

        _logger.LogInformation("OutboxWorker iniciado (intervalo {Seconds}s, lote {Batch})", _options.PollIntervalSeconds, _options.BatchSize);
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var published = await processor.ProcessBatchAsync(stoppingToken);
                if (published > 0) _logger.LogInformation("Outbox: {Count} evento(s) publicado(s)", published);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Banco indisponível etc. — o worker nunca morre, tenta no próximo ciclo.
                _logger.LogError(ex, "Falha no ciclo do OutboxWorker");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
