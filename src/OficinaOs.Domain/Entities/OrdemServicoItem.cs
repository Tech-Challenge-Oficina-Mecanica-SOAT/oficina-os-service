namespace OficinaOs.Domain.Entities;

public enum TipoOSItem
{
    Servico = 1,
    Peca = 2
}

/// <summary>
/// Item da OS. Peças e serviços são dados mestres do Cadastros; aqui guardamos só a referência (ID) e um snapshot.
/// </summary>
public class OrdemServicoItem
{
    public Guid Id { get; private set; }
    public Guid OrdemServicoId { get; private set; }
    public TipoOSItem Tipo { get; private set; }
    public Guid ReferenciaId { get; private set; }
    public string Descricao { get; private set; } = string.Empty;
    public int Quantidade { get; private set; }
    public decimal PrecoUnitario { get; private set; }
    public decimal Subtotal => Quantidade * PrecoUnitario;

    private OrdemServicoItem() { }

    public OrdemServicoItem(Guid ordemServicoId, TipoOSItem tipo, Guid referenciaId, string descricao, int quantidade, decimal precoUnitario)
    {
        if (ordemServicoId == Guid.Empty) throw new ArgumentException("OrdemServicoId é obrigatório.");
        if (referenciaId == Guid.Empty) throw new ArgumentException("ReferenciaId é obrigatório.");
        if (string.IsNullOrWhiteSpace(descricao)) throw new ArgumentException("Descrição é obrigatória.");
        if (quantidade <= 0) throw new ArgumentException("Quantidade deve ser maior que zero.");
        if (precoUnitario <= 0) throw new ArgumentException("Preço unitário deve ser maior que zero.");

        Id = Guid.NewGuid();
        OrdemServicoId = ordemServicoId;
        Tipo = tipo;
        ReferenciaId = referenciaId;
        Descricao = descricao;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }
}
