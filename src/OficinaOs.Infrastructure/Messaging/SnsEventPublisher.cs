using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using OficinaOs.Application.Abstractions;

namespace OficinaOs.Infrastructure.Messaging;

/// <summary>Publica o envelope no tópico SNS do evento. O eventType vai também como atributo (permite filtro nas subscriptions).</summary>
public class SnsEventPublisher : IEventPublisher
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly ITopicArnResolver _topics;

    public SnsEventPublisher(IAmazonSimpleNotificationService sns, ITopicArnResolver topics)
    {
        _sns = sns;
        _topics = topics;
    }

    public async Task PublishAsync(string eventType, string envelopeJson, CancellationToken ct = default)
    {
        var topicArn = await _topics.ResolveAsync(eventType, ct);
        await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = topicArn,
            Message = envelopeJson,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new() { DataType = "String", StringValue = eventType }
            }
        }, ct);
    }
}
