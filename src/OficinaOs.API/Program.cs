using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OficinaOs.API;
using OficinaOs.Application;
using OficinaOs.Infrastructure;
using OficinaOs.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Aplica migrations no startup quando habilitado (dev/compose). Em K8s fica desligado por padrão.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OsDbContext>().Database.Migrate();
}

app.UseExceptionHandler();


app.MapControllers();

// Probes do Kubernetes
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
