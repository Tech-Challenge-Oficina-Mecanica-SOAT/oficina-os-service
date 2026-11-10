namespace OficinaOs.Domain.Common;

/// <summary>Violação de regra de negócio do domínio (ex.: transição de estado inválida).</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
