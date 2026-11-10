using System.Text.Json;

namespace OficinaOs.Application.Events;

/// <summary>Evento recebido de outro serviço (envelope padrão do contrato).</summary>
public sealed record IncomingEvent(Guid EventId, string EventType, string? CorrelationId, JsonElement Data);

/// <summary>Evento ilegível ou sem campos obrigatórios. Não adianta tentar de novo: vai para a DLQ.</summary>
public class MalformedEventException : Exception
{
    public MalformedEventException(string message, Exception? inner = null) : base(message, inner) { }
}

public enum HandleResult
{
    /// <summary>Transição aplicada.</summary>
    Applied,

    /// <summary>eventId já processado antes (entrega repetida) — nada feito.</summary>
    Duplicate,

    /// <summary>Evento válido mas sem efeito (fora de ordem, OS inexistente, tipo desconhecido) — registrado e descartado.</summary>
    Ignored
}

public static class IncomingEventParser
{
    /// <summary>
    /// Lê o corpo da mensagem SQS. Aceita o envelope direto (subscription com raw_message_delivery) e também
    /// o envelope embrulhado pelo SNS ({"Type":"Notification","Message":"..."}).
    /// </summary>
    public static IncomingEvent Parse(string body)
    {
        try
        {
            using var outer = JsonDocument.Parse(body);
            var root = outer.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("Type", out var type) && type.GetString() == "Notification"
                && root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                using var inner = JsonDocument.Parse(message.GetString()!);
                return FromEnvelope(inner.RootElement);
            }

            return FromEnvelope(root);
        }
        catch (JsonException ex)
        {
            throw new MalformedEventException("Corpo da mensagem não é um JSON válido.", ex);
        }
    }

    private static IncomingEvent FromEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new MalformedEventException("Envelope deve ser um objeto JSON.");

        if (!root.TryGetProperty("eventId", out var id) || !Guid.TryParse(id.GetString(), out var eventId))
            throw new MalformedEventException("Envelope sem eventId válido.");
        if (!root.TryGetProperty("eventType", out var et) || string.IsNullOrWhiteSpace(et.GetString()))
            throw new MalformedEventException("Envelope sem eventType.");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new MalformedEventException("Envelope sem data.");

        var correlation = root.TryGetProperty("correlationId", out var c) ? c.GetString() : null;
        return new IncomingEvent(eventId, et.GetString()!, correlation, data.Clone());
    }
}
