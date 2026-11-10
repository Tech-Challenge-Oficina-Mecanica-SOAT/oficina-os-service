using OficinaOs.Application.Abstractions;
using OficinaOs.Application.Dtos;
using OficinaOs.Application.Exceptions;
using OficinaOs.Application.UseCases;
using OficinaOs.Domain.Common;
using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Tests.Unit;

internal sealed class FakeCadastros : ICadastrosClient
{
    public ClienteInfo? Cliente { get; set; }
    public VeiculoInfo? Veiculo { get; set; }

    public Task<ClienteInfo?> GetClientePorCpfAsync(string cpf, CancellationToken ct = default) => Task.FromResult(Cliente);
    public Task<VeiculoInfo?> GetVeiculoPorPlacaAsync(string placa, CancellationToken ct = default) => Task.FromResult(Veiculo);
}

internal sealed class InMemoryRepo : IOrdemServicoRepository
{
    public List<OrdemServico> Itens { get; } = new();
    public int Saves { get; private set; }

    public Task AddAsync(OrdemServico os, CancellationToken ct = default) { Itens.Add(os); return Task.CompletedTask; }
    public Task<OrdemServico?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Itens.FirstOrDefault(o => o.Id == id));
    public Task<IReadOnlyList<OrdemServico>> ListAsync(StatusOS? status, Guid? clienteId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrdemServico>>(Itens
            .Where(o => (status is null || o.Status == status) && (clienteId is null || o.ClienteId == clienteId)).ToList());
    public Task<IReadOnlyList<OrdemServico>> ListarCancelaveisDoClienteAsync(Guid clienteId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrdemServico>>(Itens.Where(o => o.ClienteId == clienteId && o.PodeCancelar).ToList());
    public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
}

public class AbrirOrdemServicoTests
{
    private readonly FakeCadastros _cadastros = new();
    private readonly InMemoryRepo _repo = new();
    private readonly Guid _clienteId = Guid.NewGuid();

    private AbrirOrdemServico Sut() => new(_cadastros, _repo);
    private static AbrirOsRequest Req() => new("12345678900", "ABC1D23", "Barulho no motor");

    private void CadastrosOk(bool ativo = true, Guid? donoDoVeiculo = null)
    {
        _cadastros.Cliente = new ClienteInfo(_clienteId, "Joao", ativo);
        _cadastros.Veiculo = new VeiculoInfo(Guid.NewGuid(), "ABC1D23", donoDoVeiculo ?? _clienteId);
    }

    [Fact]
    public async Task Abre_os_em_AguardandoDiagnostico_e_persiste()
    {
        CadastrosOk();

        var resp = await Sut().ExecuteAsync(Req());

        Assert.Equal(StatusOS.AguardandoDiagnostico, resp.Status);
        Assert.Equal(_clienteId, resp.ClienteId);
        Assert.Single(_repo.Itens);
        Assert.Equal(1, _repo.Saves);
    }

    [Fact]
    public async Task Cliente_inexistente_lanca_NotFound_e_nao_persiste()
    {
        _cadastros.Veiculo = new VeiculoInfo(Guid.NewGuid(), "ABC1D23", _clienteId);
        await Assert.ThrowsAsync<NotFoundException>(() => Sut().ExecuteAsync(Req()));
        Assert.Empty(_repo.Itens);
    }

    [Fact]
    public async Task Cliente_inativo_lanca_Forbidden()
    {
        CadastrosOk(ativo: false);
        await Assert.ThrowsAsync<ForbiddenOperationException>(() => Sut().ExecuteAsync(Req()));
        Assert.Empty(_repo.Itens);
    }

    [Fact]
    public async Task Veiculo_inexistente_lanca_NotFound()
    {
        _cadastros.Cliente = new ClienteInfo(_clienteId, "Joao", true);
        await Assert.ThrowsAsync<NotFoundException>(() => Sut().ExecuteAsync(Req()));
    }

    [Fact]
    public async Task Veiculo_de_outro_cliente_lanca_InvalidRequest()
    {
        CadastrosOk(donoDoVeiculo: Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidRequestException>(() => Sut().ExecuteAsync(Req()));
    }

    [Theory]
    [InlineData("", "ABC1D23", "x")]
    [InlineData("12345678900", " ", "x")]
    [InlineData("12345678900", "ABC1D23", "")]
    public async Task Campos_obrigatorios_ausentes_lancam_InvalidRequest(string cpf, string placa, string defeito)
    {
        CadastrosOk();
        await Assert.ThrowsAsync<InvalidRequestException>(() => Sut().ExecuteAsync(new AbrirOsRequest(cpf, placa, defeito)));
    }
}

public class CancelarEConsultarTests
{
    private readonly InMemoryRepo _repo = new();

    private OrdemServico Seed()
    {
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "Defeito");
        _repo.Itens.Add(os);
        return os;
    }

    [Fact]
    public async Task Cancelar_muda_status_e_salva()
    {
        var os = Seed();
        await new CancelarOrdemServico(_repo).ExecuteAsync(os.Id);
        Assert.Equal(StatusOS.Cancelada, os.Status);
        Assert.Equal(1, _repo.Saves);
    }

    [Fact]
    public async Task Cancelar_os_inexistente_lanca_NotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => new CancelarOrdemServico(_repo).ExecuteAsync(Guid.NewGuid()));

    [Fact]
    public async Task Cancelar_os_em_reparo_lanca_DomainException()
    {
        var os = Seed();
        os.RegistrarDiagnostico(Guid.NewGuid());
        os.RegistrarOrcamentoGerado(Guid.NewGuid());
        os.AprovarOrcamento();
        os.IniciarReparo();
        await Assert.ThrowsAsync<DomainException>(() => new CancelarOrdemServico(_repo).ExecuteAsync(os.Id));
    }

    [Fact]
    public async Task Obter_retorna_detalhes()
    {
        var os = Seed();
        var resp = await new ConsultarOrdemServico(_repo).ObterAsync(os.Id);
        Assert.Equal(os.Id, resp.Id);
        Assert.Null(resp.OrcamentoId);
    }

    [Fact]
    public async Task Obter_inexistente_lanca_NotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => new ConsultarOrdemServico(_repo).ObterAsync(Guid.NewGuid()));

    [Fact]
    public async Task Listar_filtra_por_status_e_cliente()
    {
        var a = Seed();
        var b = Seed();
        b.Cancelar(MotivoCancelamento.Outro);

        var sut = new ConsultarOrdemServico(_repo);

        Assert.Equal(2, (await sut.ListarAsync(null, null)).Count);
        Assert.Equal(a.Id, Assert.Single(await sut.ListarAsync(StatusOS.AguardandoDiagnostico, null)).Id);
        Assert.Equal(b.Id, Assert.Single(await sut.ListarAsync(null, b.ClienteId)).Id);
    }

    [Fact]
    public async Task Historico_retorna_transicoes_em_ordem()
    {
        var os = Seed();
        os.RegistrarDiagnostico(Guid.NewGuid());

        var hist = await new ConsultarOrdemServico(_repo).HistoricoAsync(os.Id);

        Assert.Equal(2, hist.Count);
        Assert.Equal(StatusOS.AguardandoDiagnostico, hist[0].StatusNovo);
        Assert.Equal(StatusOS.EmDiagnostico, hist[1].StatusNovo);
    }

    [Fact]
    public async Task Historico_de_os_inexistente_lanca_NotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => new ConsultarOrdemServico(_repo).HistoricoAsync(Guid.NewGuid()));
}
