using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaOs.Infrastructure.Messaging;
using OficinaOs.Infrastructure.Persistence;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OficinaOs.Tests.BDD.Support;

/// <summary>
/// Cenário de teste vivo: sobe a API de verdade (WebApplicationFactory, banco em memória) e um Wiremock no papel do
/// Cadastros. Os eventos dos outros serviços entram pelo mesmo caminho da fila real (SqsMessageProcessor → handler da Saga).
/// Uma instância por cenário (Reqnroll injeta e descarta).
/// </summary>
public sealed class World : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly WireMockServer _cadastros = WireMockServer.Start();
    private readonly WebApplicationFactory<Program> _factory;

    public HttpClient Http { get; }
    public Guid ClienteId { get; } = Guid.NewGuid();
    public Guid VeiculoId { get; } = Guid.NewGuid();
    public Guid? OsId { get; set; }
    public HttpResponseMessage? LastResponse { get; set; }

    public World()
    {
        var dbName = Guid.NewGuid().ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cadastros:BaseUrl"] = _cadastros.Url
            }));
            b.ConfigureServices(s =>
            {
                var existentes = s.Where(d => d.ServiceType == typeof(DbContextOptions<OsDbContext>)
                                           || d.ServiceType == typeof(DbContextOptions)
                                           || (d.ServiceType.IsGenericType
                                               && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                                               && d.ServiceType.GenericTypeArguments[0] == typeof(OsDbContext))).ToList();
                foreach (var d in existentes) s.Remove(d);
                s.AddDbContext<OsDbContext>(o => o.UseInMemoryDatabase(dbName));
            });
        });
        Http = _factory.CreateClient();
    }

    public void Dispose()
    {
        LastResponse?.Dispose();
        Http.Dispose();
        _factory.Dispose();
        _cadastros.Stop();
    }

    // ---- Cadastros (Wiremock) ----------------------------------------------------------------

    public void CadastrarCliente(string cpf, bool ativo)
    {
        _cadastros.Given(Request.Create().WithPath($"/clientes/cpf/{cpf}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("{\"id\":\"" + ClienteId + "\",\"nome\":\"Cliente Teste\",\"cpf\":\"" + cpf +
                          "\",\"email\":\"c@teste.com\",\"ativo\":" + (ativo ? "true" : "false") + "}"));
    }

    public void CadastrarVeiculo(string placa)
    {
        _cadastros.Given(Request.Create().WithPath($"/veiculos/placa/{placa}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("{\"id\":\"" + VeiculoId + "\",\"placa\":\"" + placa +
                          "\",\"modelo\":\"Gol\",\"marca\":\"VW\",\"ano\":2020,\"clienteId\":\"" + ClienteId + "\"}"));
    }

    // ---- API da OS ----------------------------------------------------------------------------

    public async Task AbrirOsAsync(string cpf, string placa, string defeito)
    {
        LastResponse = await Http.PostAsJsonAsync("/ordens-servico", new { cpf, placa, defeitoRelatado = defeito });
        if (LastResponse.IsSuccessStatusCode)
            OsId = (await Json(LastResponse)).GetProperty("id").GetGuid();
    }

    public async Task<string> StatusAsync() =>
        (await Json(await Http.GetAsync($"/ordens-servico/{OsId}"))).GetProperty("status").GetString()!;

    public async Task<int> QuantidadeHistoricoAsync() =>
        (await Json(await Http.GetAsync($"/ordens-servico/{OsId}/historico"))).GetArrayLength();

    public async Task<HttpResponseMessage> CancelarAsync() =>
        LastResponse = await Http.PostAsync($"/ordens-servico/{OsId}/cancelar", null);

    public static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Web);

    // ---- Eventos dos outros serviços (entram como se viessem da fila SQS) ------------------------

    public async Task PublicarEventoAsync(string eventType, object data, Guid? eventId = null)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            eventId = eventId ?? Guid.NewGuid(), eventType, eventVersion = "1.0", occurredAt = DateTime.UtcNow,
            correlationId = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            producer = "bdd", data
        });
        await _factory.Services.GetRequiredService<SqsMessageProcessor>().ProcessAsync(envelope);
    }

    // ---- Banco (verificações) --------------------------------------------------------------------

    public async Task<List<string>> EventosNaOutboxAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
        return await db.OutboxMessages.OrderBy(m => m.OccurredAt).Select(m => m.EventType).ToListAsync();
    }

    public async Task<string?> PayloadDoEventoAsync(string eventType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
        return await db.OutboxMessages.Where(m => m.EventType == eventType).Select(m => m.Payload).FirstOrDefaultAsync();
    }
}
