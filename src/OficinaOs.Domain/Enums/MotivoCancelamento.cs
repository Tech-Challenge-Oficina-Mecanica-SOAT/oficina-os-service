namespace OficinaOs.Domain.Enums;

/// <summary>Valores aceitos em os.cancelada.motivo (contratos/eventos/schemas.md).</summary>
public enum MotivoCancelamento
{
    OrcamentoRecusado = 1,
    ClienteDesativado = 2,
    Outro = 3
}

public static class MotivoCancelamentoExtensions
{
    public static string ToContrato(this MotivoCancelamento m) => m switch
    {
        MotivoCancelamento.OrcamentoRecusado => "orcamento_recusado",
        MotivoCancelamento.ClienteDesativado => "cliente_desativado",
        _ => "outro"
    };
}
