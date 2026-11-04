namespace OficinaOs.Domain.Enums;

/// <summary>
/// Estados da OS na Fase 4 (contrato: contratos/openapi/os.yaml).
/// </summary>
public enum StatusOS
{
    AguardandoDiagnostico = 1,
    EmDiagnostico = 2,
    AguardandoAprovacao = 3,
    Aprovada = 4,
    EmReparo = 5,
    AguardandoPagamento = 6,
    Concluida = 7,
    Cancelada = 8,
    PendenteCobranca = 9
}
