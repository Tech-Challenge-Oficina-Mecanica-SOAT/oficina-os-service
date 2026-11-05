using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OficinaOs.Infrastructure.Persistence;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OficinaOs.Tests.Unit;

/// <summary>
/// Sobe a API de verdade (WebApplicationFactory) com banco em memória e o Cadastros simulado por Wiremock.
/// Exercita HTTP → controller → use case → repositório → cliente HTTP do Cadastros.
/// </summary>
public sealed class ApiEndToEndTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly WireMockServer _cadastros = WireMockServer.Start();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _veiculoId = Guid.NewGuid();

    public ApiEndToEndTests()
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
                // EF Core 9+ guarda a configuração do provider (Npgsql) em IDbContextOptionsConfiguration<T>;
                // precisa sair junto, senão Npgsql e InMemory conflitam.
                var existentes = s.Where(d => d.ServiceType == typeof(DbContextOptions<OsDbContext>)
                                           || d.ServiceType == typeof(DbContextOptions)
                                           || (d.ServiceType.IsGenericType
                                               && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                                               && d.ServiceType.GenericTypeArguments[0] == typeof(OsDbContext))).ToList();
                foreach (var d in existentes) s.Remove(d);
                s.AddDbContext<OsDbContext>(o => o.UseInMemoryDatabase(dbName));
            });
        });
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
        _cadastros.Stop();
    }

    private void StubCadastros(bool clienteAtivo = true, Guid? donoDoVeiculo = null)
    {
        _cadastros.Given(Request.Create().WithPath("/clientes/cpf/12345678900").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"id":"{{_clienteId}}","nome":"Joao","cpf":"12345678900","email":"j@t.com","ativo":{{clienteAtivo.ToString().ToLower()}}}"""));
        _cadastros.Given(Request.Create().WithPath("/veiculos/placa/ABC1D23").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"id":"{{_veiculoId}}","placa":"ABC1D23","modelo":"Gol","marca":"VW","ano":2020,"clienteId":"{{donoDoVeiculo ?? _clienteId}}"}"""));
    }

    private Task<HttpResponseMessage> AbrirOs() =>
        _http.PostAsJsonAsync("/ordens-servico", new { cpf = "12345678900", placa = "ABC1D23", defeitoRelatado = "Barulho no motor" });

    private static async Task<JsonElement> Body(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Web);

    [Fact]
    public async Task Abrir_os_retorna_201_com_status_AguardandoDiagnostico()
    {
        StubCadastros();

        var resp = await AbrirOs();

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await Body(resp);
        Assert.Equal("AguardandoDiagnostico", body.GetProperty("status").GetString());
        Assert.Equal(_clienteId, body.GetProperty("clienteId").GetGuid());
        Assert.Equal(_veiculoId, body.GetProperty("veiculoId").GetGuid());
    }

    [Fact]
    public async Task Cliente_inexistente_retorna_404()
    {
        // sem stubs: o Wiremock responde 404 para tudo
        var resp = await AbrirOs();
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Cliente_inativo_retorna_403()
    {
        StubCadastros(clienteAtivo: false);
        var resp = await AbrirOs();
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Veiculo_de_outro_cliente_retorna_400()
    {
        StubCadastros(donoDoVeiculo: Guid.NewGuid());
        var resp = await AbrirOs();
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Body_sem_campos_retorna_400()
    {
        var resp = await _http.PostAsJsonAsync("/ordens-servico", new { cpf = "", placa = "", defeitoRelatado = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cadastros_fora_do_ar_retorna_503()
    {
        _cadastros.Given(Request.Create().WithPath("/clientes/cpf/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("erro"));

        var resp = await AbrirOs();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        var detail = (await Body(resp)).GetProperty("detail").GetString();
        Assert.Equal("Cadastros indisponível no momento. Tente novamente.", detail);
        Assert.DoesNotContain("127.0.0.1", detail);
    }

    [Fact]
    public async Task Consultar_listar_historico_e_cancelar()
    {
        StubCadastros();
        var criada = await Body(await AbrirOs());
        var id = criada.GetProperty("id").GetGuid();

        var get = await _http.GetAsync($"/ordens-servico/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Body(get)).GetProperty("orcamentoId").ValueKind);

        var lista = await Body(await _http.GetAsync("/ordens-servico?status=AguardandoDiagnostico"));
        Assert.Equal(1, lista.GetArrayLength());

        var cancel = await _http.PostAsync($"/ordens-servico/{id}/cancelar", null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var historico = await Body(await _http.GetAsync($"/ordens-servico/{id}/historico"));
        Assert.Equal(2, historico.GetArrayLength());
        Assert.Equal("Cancelada", historico[1].GetProperty("statusNovo").GetString());

        var cancelDeNovo = await _http.PostAsync($"/ordens-servico/{id}/cancelar", null);
        Assert.Equal(HttpStatusCode.Conflict, cancelDeNovo.StatusCode);
    }

    [Fact]
    public async Task Os_inexistente_retorna_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/ordens-servico/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync($"/ordens-servico/{Guid.NewGuid()}/cancelar", null)).StatusCode);
    }

    [Fact]
    public async Task Health_live_responde_200() =>
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/health/live")).StatusCode);
}
