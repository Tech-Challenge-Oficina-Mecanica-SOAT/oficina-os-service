namespace OficinaOs.Infrastructure.Messaging;

public class MessagingOptions
{
    public const string Section = "Messaging";

    /// <summary>Liga o worker do Outbox e o publisher SNS. Desligado: eventos ficam pendentes na tabela.</summary>
    public bool Enabled { get; set; }

    /// <summary>homolog | prod — compõe o caminho do Parameter Store (/oficina/{env}/messaging/topics/...).</summary>
    public string Environment { get; set; } = "homolog";

    public string Region { get; set; } = "us-east-1";

    /// <summary>Endpoint alternativo (LocalStack em dev). Vazio = AWS real.</summary>
    public string? ServiceUrl { get; set; }

    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 20;

    /// <summary>Depois de N falhas a mensagem é "estacionada" (para de tentar) e fica registrada em LastError.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>URL da fila do OS (consumo). Vazio = resolve pelo QueueName.</summary>
    public string? QueueUrl { get; set; }

    /// <summary>Nome da fila do OS. Vazio = oficina-{Environment}-os-queue.</summary>
    public string? QueueName { get; set; }

    /// <summary>Long polling do SQS (0-20s).</summary>
    public int WaitTimeSeconds { get; set; } = 10;

    public int MaxMessages { get; set; } = 10;

    /// <summary>Sobrescreve a descoberta por Parameter Store: { "os.criada": "arn:aws:sns:..." }.</summary>
    public Dictionary<string, string> TopicArns { get; set; } = new();
}
