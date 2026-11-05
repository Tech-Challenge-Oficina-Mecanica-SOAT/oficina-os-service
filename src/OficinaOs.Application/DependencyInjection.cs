using Microsoft.Extensions.DependencyInjection;
using OficinaOs.Application.UseCases;

namespace OficinaOs.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AbrirOrdemServico>();
        services.AddScoped<CancelarOrdemServico>();
        services.AddScoped<ConsultarOrdemServico>();
        return services;
    }
}
