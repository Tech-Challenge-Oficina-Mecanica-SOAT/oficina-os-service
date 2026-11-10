using OficinaOs.Infrastructure.Cadastros;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OficinaOs.Tests.Unit;

/// <summary>
/// Testa o cliente HTTP do Cadastros contra um servidor Wiremock real (em memória, porta aleatória),
/// que imita o contrato de contratos/openapi/cadastros.yaml. Não depende do serviço do Pedro estar no ar.
/// </summary>
public sealed class CadastrosHttpClientTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private CadastrosHttpClient Sut() =>
        new(new HttpClient { BaseAddress = new Uri(_server.Url!.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) });

    public void Dispose() => _server.Stop();

    [Fact]
    public async Task Cliente_existente_e_mapeado()
    {
        var id = Guid.NewGuid();
        _server.Given(Request.Create().WithPath("/clientes/cpf/12345678900").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"id":"{{id}}","nome":"Joao Silva","cpf":"12345678900","email":"joao@teste.com","ativo":true}"""));

        var cliente = await Sut().GetClientePorCpfAsync("12345678900");

        Assert.NotNull(cliente);
        Assert.Equal(id, cliente!.Id);
        Assert.Equal("Joao Silva", cliente.Nome);
        Assert.True(cliente.Ativo);
    }

    [Fact]
    public async Task Cliente_inativo_vem_com_Ativo_false()
    {
        _server.Given(Request.Create().WithPath("/clientes/cpf/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"id":"{{Guid.NewGuid()}}","nome":"Maria","ativo":false}"""));

        var cliente = await Sut().GetClientePorCpfAsync("00000000000");

        Assert.False(cliente!.Ativo);
    }

    [Fact]
    public async Task Cliente_404_retorna_null()
    {
        _server.Given(Request.Create().WithPath("/clientes/cpf/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        Assert.Null(await Sut().GetClientePorCpfAsync("99999999999"));
    }

    [Fact]
    public async Task Veiculo_existente_e_mapeado()
    {
        var id = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        _server.Given(Request.Create().WithPath("/veiculos/placa/ABC1D23").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"id":"{{id}}","placa":"ABC1D23","modelo":"Gol","marca":"VW","ano":2020,"clienteId":"{{clienteId}}"}"""));

        var veiculo = await Sut().GetVeiculoPorPlacaAsync("ABC1D23");

        Assert.NotNull(veiculo);
        Assert.Equal(id, veiculo!.Id);
        Assert.Equal(clienteId, veiculo.ClienteId);
    }

    [Fact]
    public async Task Veiculo_404_retorna_null()
    {
        _server.Given(Request.Create().WithPath("/veiculos/placa/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        Assert.Null(await Sut().GetVeiculoPorPlacaAsync("ZZZ0Z00"));
    }

    [Fact]
    public async Task Cadastros_fora_do_ar_propaga_HttpRequestException()
    {
        _server.Given(Request.Create().WithPath("/clientes/cpf/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("erro"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Sut().GetClientePorCpfAsync("12345678900"));
    }
}
