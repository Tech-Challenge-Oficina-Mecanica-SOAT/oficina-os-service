using System.Text.Json;
using Microsoft.Extensions.Logging;
using OficinaOs.Application.Abstractions;
using OficinaOs.Domain.Common;
using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Application.Events;

/// <summary>
/// Coração da orquestração da Saga: recebe o FATO publicado por outro serviço e decide o que ele significa para a OS.
///
/// Garantias:
/// - Idempotente: o mesmo eventId nunca é aplicado duas vezes (registro em processed_events, na mesma transação).
/// - Tolerante a desordem: transição inválida para o estado atual é registrada e descartada (não vai para a DLQ).
/// - Compensação: orcamento.recusado e cliente.desativado cancelam a OS; o os.cancelada sai pelo Outbox.
/// Falha técnica (banco fora do ar) lança exceção → a mensagem volta para a fila e, após 3 tentativas, vai para a DLQ.
/// </summary>
public class IncomingEventHandler
{
    private readonly IOrdemServicoRepository _repo;
    private readonly IProcessedEventStore _processed;
    private readonly ILogger<IncomingEventHandler> _logger;

    public IncomingEventHandler(IOrdemServicoRepository repo, IProcessedEventStore processed, ILogger<IncomingEventHandler> logger)
    {
        _repo = repo;
        _processed = processed;
        _logger = logger;
    }

    public static readonly string[] SupportedEvents =
    {
        "diagnostico.concluido", "orcamento.gerado", "orcamento.aprovado", "orcamento.recusado",
        "execucao.iniciada", "execucao.finalizada", "pagamento.confirmado", "pagamento.recusado",
        "cliente.desativado"
    };

    public async Task<HandleResult> HandleAsync(IncomingEvent evt, CancellationToken ct = default)
    {
        if (await _processed.ExistsAsync(evt.EventId, ct))
        {
            _logger.LogInformation("Evento {EventId} ({EventType}) já processado — ignorando entrega repetida", evt.EventId, evt.EventType);
            return HandleResult.Duplicate;
        }

        HandleResult result;
        try
        {
            result = await ApplyAsync(evt, ct);
        }
        catch (DomainException ex)
        {
            // Evento fora de ordem / estado não permite: não é falha técnica, retentar não resolve.
            _logger.LogWarning("Evento {EventId} ({EventType}) sem efeito: {Motivo}", evt.EventId, evt.EventType, ex.Message);
            result = HandleResult.Ignored;
        }

        _processed.Add(evt.EventId, evt.EventType, result.ToString());
        await _repo.SaveChangesAsync(ct);
        return result;
    }

    private async Task<HandleResult> ApplyAsync(IncomingEvent evt, CancellationToken ct)
    {
        if (evt.EventType == "cliente.desativado")
            return await CancelarOsDoClienteAsync(evt, ct);

        if (!SupportedEvents.Contains(evt.EventType))
        {
            _logger.LogWarning("Tipo de evento desconhecido: {EventType} ({EventId})", evt.EventType, evt.EventId);
            return HandleResult.Ignored;
        }

        var osId = RequireGuid(evt, "osId");
        var os = await _repo.GetByIdAsync(osId, ct);
        if (os is null)
        {
            _logger.LogWarning("Evento {EventType} ({EventId}) para OS inexistente {OsId}", evt.EventType, evt.EventId, osId);
            return HandleResult.Ignored;
        }

        var origem = evt.EventType;
        switch (evt.EventType)
        {
            case "diagnostico.concluido": os.RegistrarDiagnostico(RequireGuid(evt, "diagnosticoId"), origem); break;
            case "orcamento.gerado": os.RegistrarOrcamentoGerado(RequireGuid(evt, "orcamentoId"), origem); break;
            case "orcamento.aprovado": os.AprovarOrcamento(origem); break;
            case "orcamento.recusado": os.Cancelar(MotivoCancelamento.OrcamentoRecusado, origem); break;
            case "execucao.iniciada": os.IniciarReparo(origem); break;
            case "execucao.finalizada": os.FinalizarReparo(origem); break;
            case "pagamento.confirmado": os.ConfirmarPagamento(RequireGuid(evt, "pagamentoId"), origem); break;
            case "pagamento.recusado": os.RecusarPagamento(origem); break;
        }

        _logger.LogInformation("Evento {EventType} aplicado à OS {OsId}: agora {Status}", evt.EventType, os.Id, os.Status);
        return HandleResult.Applied;
    }

    /// <summary>Compensação: cliente desativado cancela todas as OSes dele que ainda podem ser canceladas.</summary>
    private async Task<HandleResult> CancelarOsDoClienteAsync(IncomingEvent evt, CancellationToken ct)
    {
        var clienteId = RequireGuid(evt, "clienteId");
        var abertas = await _repo.ListarCancelaveisDoClienteAsync(clienteId, ct);
        foreach (var os in abertas)
            os.Cancelar(MotivoCancelamento.ClienteDesativado, evt.EventType);

        _logger.LogInformation("cliente.desativado: {Qtd} OS(es) do cliente {ClienteId} canceladas", abertas.Count, clienteId);
        return abertas.Count > 0 ? HandleResult.Applied : HandleResult.Ignored;
    }

    private static Guid RequireGuid(IncomingEvent evt, string property)
    {
        if (evt.Data.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var guid)
            && guid != Guid.Empty)
            return guid;

        throw new MalformedEventException($"Evento {evt.EventType} sem '{property}' válido.");
    }
}
