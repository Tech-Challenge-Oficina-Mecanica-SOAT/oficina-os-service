using Microsoft.Extensions.DependencyInjection;
using OficinaOs.Application.Events;
using OficinaOs.Application.UseCases;

namespace OficinaOs.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AbrirOrdemServico>();
        services.AddScoped<CancelarOrdemServico>();
        services.AddScoped<ConsultarOrdemServico>();
        services.AddScoped<IncomingEventHandler>();
        return services;
    }
}
