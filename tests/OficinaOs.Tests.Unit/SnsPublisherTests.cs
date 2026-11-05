using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.Options;
using OficinaOs.Infrastructure.Messaging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OficinaOs.Tests.Unit;

/// <summary>
/// Testa o publisher SNS e o resolvedor de ARN contra Wiremock fingindo ser a AWS
/// (o SDK aponta para ServiceURL local). Sem rede e sem credenciais reais.
/// </summary>
public sealed class SnsPublisherTests : IDisposable
{
    private const string TopicArn = "arn:aws:sns:us-east-1:000000000000:oficina-homolog-os-criada";
    private readonly WireMockServer _aws = WireMockServer.Start();
    private readonly BasicAWSCredentials _creds = new("test", "test");

    public void Dispose() => _aws.Stop();

    private AmazonSimpleNotificationServiceClient Sns() => new(_creds,
        new AmazonSimpleNotificationServiceConfig { RegionEndpoint = RegionEndpoint.USEast1, ServiceURL = _aws.Url });

    private AmazonSimpleSystemsManagementClient Ssm() => new(_creds,
        new AmazonSimpleSystemsManagementConfig { RegionEndpoint = RegionEndpoint.USEast1, ServiceURL = _aws.Url });

    private static IOptions<MessagingOptions> Opts(Dictionary<string, string>? arns = null) =>
        Options.Create(new MessagingOptions { Environment = "homolog", TopicArns = arns ?? new() });

    [Fact]
    public async Task Resolver_usa_configuracao_explicita_sem_chamar_a_AWS()
    {
        var resolver = new TopicArnResolver(Opts(new() { ["os.criada"] = TopicArn }), Ssm());

        Assert.Equal(TopicArn, await resolver.ResolveAsync("os.criada"));
        Assert.Empty(_aws.LogEntries);
    }

    [Fact]
    public async Task Resolver_busca_no_Parameter_Store_e_faz_cache()
    {
        _aws.Given(Request.Create().UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/x-amz-json-1.1")
                .WithBody("{\"Parameter\":{\"Name\":\"x\",\"Type\":\"String\",\"Value\":\"" + TopicArn + "\"}}"));
        var resolver = new TopicArnResolver(Opts(), Ssm());

        var primeira = await resolver.ResolveAsync("os.criada");
        var segunda = await resolver.ResolveAsync("os.criada");

        Assert.Equal(TopicArn, primeira);
        Assert.Equal(TopicArn, segunda);
        Assert.Single(_aws.LogEntries); // segunda veio do cache
        Assert.Contains("/oficina/homolog/messaging/topics/os.criada/arn", _aws.LogEntries.Single().RequestMessage.Body);
    }

    [Fact]
    public void Nome_do_parametro_segue_o_padrao_do_schemas_md() =>
        Assert.Equal("/oficina/prod/messaging/topics/os.cancelada/arn",
            TopicArnResolver.ParameterName("prod", "os.cancelada"));

    [Fact]
    public async Task Publisher_envia_envelope_ao_topico_com_atributo_eventType()
    {
        _aws.Given(Request.Create().UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "text/xml")
                .WithBody("""<PublishResponse xmlns="http://sns.amazonaws.com/doc/2010-03-31/"><PublishResult><MessageId>m-1</MessageId></PublishResult><ResponseMetadata><RequestId>r-1</RequestId></ResponseMetadata></PublishResponse>"""));
        var publisher = new SnsEventPublisher(Sns(),
            new TopicArnResolver(Opts(new() { ["os.criada"] = TopicArn }), Ssm()));

        await publisher.PublishAsync("os.criada", """{"eventId":"abc","eventType":"os.criada"}""");

        var body = Uri.UnescapeDataString(_aws.LogEntries.Single().RequestMessage.Body!.Replace('+', ' '));
        Assert.Contains("Action=Publish", body);
        Assert.Contains(TopicArn, body);
        Assert.Contains("\"eventType\":\"os.criada\"", body);
        Assert.Contains("MessageAttributes.entry.1.Name=eventType", body);
        Assert.Contains("MessageAttributes.entry.1.Value.StringValue=os.criada", body);
    }

    [Fact]
    public async Task Publisher_propaga_erro_da_AWS_para_o_outbox_tentar_de_novo()
    {
        _aws.Given(Request.Create().UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("erro"));
        var publisher = new SnsEventPublisher(Sns(),
            new TopicArnResolver(Opts(new() { ["os.criada"] = TopicArn }), Ssm()));

        await Assert.ThrowsAnyAsync<Exception>(() => publisher.PublishAsync("os.criada", "{}"));
    }
}
