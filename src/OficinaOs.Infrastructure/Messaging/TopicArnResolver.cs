using System.Collections.Concurrent;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Options;

namespace OficinaOs.Infrastructure.Messaging;

public interface ITopicArnResolver
{
    Task<string> ResolveAsync(string eventType, CancellationToken ct = default);
}

/// <summary>
/// Descobre o ARN do tópico do evento: primeiro a configuração explícita (Messaging:TopicArns),
/// depois o Parameter Store (/oficina/{env}/messaging/topics/{evento}/arn, publicado pelo Terraform do dono do tópico).
/// </summary>
public class TopicArnResolver : ITopicArnResolver
{
    private readonly MessagingOptions _options;
    private readonly IAmazonSimpleSystemsManagement _ssm;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public TopicArnResolver(IOptions<MessagingOptions> options, IAmazonSimpleSystemsManagement ssm)
    {
        _options = options.Value;
        _ssm = ssm;
    }

    public static string ParameterName(string environment, string eventType) =>
        $"/oficina/{environment}/messaging/topics/{eventType}/arn";

    public async Task<string> ResolveAsync(string eventType, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(eventType, out var cached)) return cached;

        if (_options.TopicArns.TryGetValue(eventType, out var configured) && !string.IsNullOrWhiteSpace(configured))
            return _cache[eventType] = configured;

        var response = await _ssm.GetParameterAsync(
            new GetParameterRequest { Name = ParameterName(_options.Environment, eventType) }, ct);
        return _cache[eventType] = response.Parameter.Value;
    }
}
