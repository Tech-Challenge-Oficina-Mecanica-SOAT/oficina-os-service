using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaOs.Application.Abstractions;

namespace OficinaOs.Infrastructure.Cadastros;

public class CadastrosOptions
{
    public const string Section = "Cadastros";

    /// <summary>URL base do Cadastros Service (em produção vem do Parameter Store; em dev/teste aponta pro Wiremock).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8081";
    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// Cliente REST do Cadastros (contrato: contratos/openapi/cadastros.yaml).
/// 404 vira null; qualquer outra falha (5xx, timeout) propaga HttpRequestException e a API responde 503.
/// </summary>
public class CadastrosHttpClient : ICadastrosClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public CadastrosHttpClient(HttpClient http) => _http = http;

    public async Task<ClienteInfo?> GetClientePorCpfAsync(string cpf, CancellationToken ct = default)
    {
        var dto = await GetOrNullAsync<ClienteDto>($"clientes/cpf/{Uri.EscapeDataString(cpf)}", ct);
        return dto is null ? null : new ClienteInfo(dto.Id, dto.Nome ?? string.Empty, dto.Ativo);
    }

    public async Task<VeiculoInfo?> GetVeiculoPorPlacaAsync(string placa, CancellationToken ct = default)
    {
        var dto = await GetOrNullAsync<VeiculoDto>($"veiculos/placa/{Uri.EscapeDataString(placa)}", ct);
        return dto is null ? null : new VeiculoInfo(dto.Id, dto.Placa ?? placa, dto.ClienteId);
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private sealed record ClienteDto(Guid Id, string? Nome, string? Cpf, bool Ativo);
    private sealed record VeiculoDto(Guid Id, string? Placa, Guid ClienteId);
}
