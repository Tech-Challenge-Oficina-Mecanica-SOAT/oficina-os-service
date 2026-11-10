using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OficinaOs.Domain.Common;
using OficinaOs.Domain.Enums;
using OficinaOs.Domain.Events;

namespace OficinaOs.Infrastructure.Messaging;

/// <summary>Envelope padrão de todos os eventos (contratos/eventos/schemas.md).</summary>
public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    string EventVersion,
    DateTime OccurredAt,
    string CorrelationId,
    string Producer,
    object Data);

/// <summary>Traduz eventos de domínio da OS em eventos de integração (envelope + payload do contrato).</summary>
public static class IntegrationEventMapper
{
    public const string Producer = "os-service";
    public const string EventVersion = "1.0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static (string EventType, string EnvelopeJson) ToEnvelope(IDomainEvent evt, string correlationId)
    {
        var (eventType, data) = evt switch
        {
            OsCriadaEvent e => ("os.criada", (object)new
            {
                osId = e.OsId,
                clienteId = e.ClienteId,
                veiculoId = e.VeiculoId,
                defeitoRelatado = e.DefeitoRelatado,
                criadaEm = e.CriadaEm
            }),
            OsCanceladaEvent e => ("os.cancelada", new
            {
                osId = e.OsId,
                motivo = e.Motivo.ToContrato(),
                canceladaEm = e.CanceladaEm
            }),
            _ => throw new NotSupportedException($"Evento de domínio sem mapeamento de integração: {evt.GetType().Name}")
        };

        var envelope = new EventEnvelope(evt.EventId, eventType, EventVersion, evt.OccurredAt, correlationId, Producer, data);
        return (eventType, JsonSerializer.Serialize(envelope, Json));
    }

    /// <summary>
    /// correlationId = traceparent W3C da requisição em andamento (ASP.NET Core cria um Activity por request).
    /// Fora de uma requisição, gera um novo para o fluxo continuar rastreável.
    /// </summary>
    public static string CurrentCorrelationId()
    {
        var current = Activity.Current;
        if (current is { IdFormat: ActivityIdFormat.W3C }) return current.Id!;
        return $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
    }
}
