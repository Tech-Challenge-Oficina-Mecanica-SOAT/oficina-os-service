using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OficinaOs.Application.Abstractions;
using OficinaOs.Infrastructure.Cadastros;
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

        return services;
    }
}
