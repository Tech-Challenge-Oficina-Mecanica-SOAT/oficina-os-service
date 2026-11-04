using OficinaOs.Domain.Entities;
using OficinaOs.Domain.Enums;

namespace OficinaOs.Tests.Unit;

public class OrdemServicoTests
{
    [Fact]
    public void Nova_os_comeca_em_AguardandoDiagnostico_com_historico_inicial()
    {
        var os = new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), "Barulho no motor");

        Assert.Equal(StatusOS.AguardandoDiagnostico, os.Status);
        var h = Assert.Single(os.Historico);
        Assert.Null(h.StatusAnterior);
        Assert.Equal(StatusOS.AguardandoDiagnostico, h.StatusNovo);
    }

    [Fact]
    public void Cliente_vazio_lanca_excecao() =>
        Assert.Throws<ArgumentException>(() => new OrdemServico(Guid.Empty, Guid.NewGuid(), "x"));

    [Fact]
    public void Veiculo_vazio_lanca_excecao() =>
        Assert.Throws<ArgumentException>(() => new OrdemServico(Guid.NewGuid(), Guid.Empty, "x"));

    [Fact]
    public void Defeito_em_branco_lanca_excecao() =>
        Assert.Throws<ArgumentException>(() => new OrdemServico(Guid.NewGuid(), Guid.NewGuid(), " "));
}

public class OrdemServicoItemTests
{
    [Fact]
    public void Subtotal_e_quantidade_vezes_preco()
    {
        var item = new OrdemServicoItem(Guid.NewGuid(), TipoOSItem.Peca, Guid.NewGuid(), "Filtro", 2, 50m);
        Assert.Equal(100m, item.Subtotal);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void Quantidade_ou_preco_invalidos_lancam_excecao(int qtd, int preco) =>
        Assert.Throws<ArgumentException>(() =>
            new OrdemServicoItem(Guid.NewGuid(), TipoOSItem.Servico, Guid.NewGuid(), "Troca", qtd, preco));
}

public class HistoricoStatusOSTests
{
    [Fact]
    public void Status_anterior_igual_ao_novo_lanca_excecao() =>
        Assert.Throws<ArgumentException>(() =>
            new HistoricoStatusOS(Guid.NewGuid(), StatusOS.Concluida, StatusOS.Concluida, "sistema", "x"));
}
