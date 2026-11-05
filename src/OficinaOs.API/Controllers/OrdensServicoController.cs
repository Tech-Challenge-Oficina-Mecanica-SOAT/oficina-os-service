using Microsoft.AspNetCore.Mvc;
using OficinaOs.Application.Dtos;
using OficinaOs.Application.UseCases;
using OficinaOs.Domain.Enums;

namespace OficinaOs.API.Controllers;

[ApiController]
[Route("ordens-servico")]
public class OrdensServicoController : ControllerBase
{
    /// <summary>Abre uma nova OS (início da Saga). Valida cliente e veículo no Cadastros.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OsResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Abrir([FromBody] AbrirOsRequest request, [FromServices] AbrirOrdemServico useCase, CancellationToken ct)
    {
        var os = await useCase.ExecuteAsync(request, ct);
        return CreatedAtAction(nameof(Obter), new { id = os.Id }, os);
    }

    /// <summary>Lista OSes, com filtros opcionais por status e cliente.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OsResponse>>> Listar(
        [FromQuery] StatusOS? status, [FromQuery] Guid? clienteId,
        [FromServices] ConsultarOrdemServico useCase, CancellationToken ct) =>
        Ok(await useCase.ListarAsync(status, clienteId, ct));

    /// <summary>Detalhes da OS (consumido por Billing e Execução).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OsDetalhadaResponse>> Obter(
        Guid id, [FromServices] ConsultarOrdemServico useCase, CancellationToken ct) =>
        Ok(await useCase.ObterAsync(id, ct));

    /// <summary>Cancela a OS (compensação). Só permitido até a OS estar Aprovada.</summary>
    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, [FromServices] CancelarOrdemServico useCase, CancellationToken ct)
    {
        await useCase.ExecuteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Histórico de mudanças de status.</summary>
    [HttpGet("{id:guid}/historico")]
    public async Task<ActionResult<IReadOnlyList<HistoricoStatusResponse>>> Historico(
        Guid id, [FromServices] ConsultarOrdemServico useCase, CancellationToken ct) =>
        Ok(await useCase.HistoricoAsync(id, ct));
}
