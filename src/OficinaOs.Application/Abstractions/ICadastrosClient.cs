namespace OficinaOs.Application.Abstractions;

public sealed record ClienteInfo(Guid Id, string Nome, bool Ativo);

public sealed record VeiculoInfo(Guid Id, string Placa, Guid ClienteId);

/// <summary>
/// Porta para o Cadastros Service (REST síncrono). A OS nunca acessa o banco do Cadastros:
/// só consulta via API e guarda os IDs. Retorna null quando o recurso não existe (404).
/// </summary>
public interface ICadastrosClient
{
    Task<ClienteInfo?> GetClientePorCpfAsync(string cpf, CancellationToken ct = default);
    Task<VeiculoInfo?> GetVeiculoPorPlacaAsync(string placa, CancellationToken ct = default);
}
