using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleSystemsManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OficinaOs.Application.Abstractions;
using OficinaOs.Infrastructure.Cadastros;
using OficinaOs.Infrastructure.Messaging;
using OficinaOs.Infrastructure.Persistence;

namespace OficinaOs.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<OsDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.")));
        services.AddScoped<IOrdemServicoRepository, OrdemServicoRepository>();

        // Options lidas de forma preguiçosa (na hora de criar o HttpClient), assim testes e
        // variáveis de ambiente podem sobrescrever Cadastros:BaseUrl depois do registro.
        services.Configure<CadastrosOptions>(config.GetSection(CadastrosOptions.Section));
        services.AddHttpClient<ICadastrosClient, CadastrosHttpClient>((sp, c) =>
        {
            var options = sp.GetRequiredService<IOptions<CadastrosOptions>>().Value;
            c.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        AddMessaging(services, config);
        return services;
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MessagingOptions>(config.GetSection(MessagingOptions.Section));

        // Clientes AWS criados de forma preguiçosa a partir das options (LocalStack em dev, AWS real em K8s).
        services.AddSingleton<IAmazonSimpleNotificationService>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var cfg = new AmazonSimpleNotificationServiceConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region) };
            if (!string.IsNullOrWhiteSpace(o.ServiceUrl)) cfg.ServiceURL = o.ServiceUrl;
            return new AmazonSimpleNotificationServiceClient(cfg);
        });
        services.AddSingleton<IAmazonSimpleSystemsManagement>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var cfg = new AmazonSimpleSystemsManagementConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region) };
            if (!string.IsNullOrWhiteSpace(o.ServiceUrl)) cfg.ServiceURL = o.ServiceUrl;
            return new AmazonSimpleSystemsManagementClient(cfg);
        });

        services.AddSingleton<ITopicArnResolver, TopicArnResolver>();
        services.AddSingleton<IEventPublisher, SnsEventPublisher>();
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxWorker>();
    }
}
