using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OficinaOs.Application.Exceptions;
using OficinaOs.Domain.Common;

namespace OficinaOs.API;

/// <summary>Traduz exceções de negócio em respostas HTTP (ProblemDetails), conforme o os.yaml.</summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
            ForbiddenOperationException => (StatusCodes.Status403Forbidden, "Operação não permitida"),
            InvalidRequestException or ArgumentException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
            DomainException => (StatusCodes.Status409Conflict, "Estado da OS não permite a operação"),
            HttpRequestException or TaskCanceledException => (StatusCodes.Status503ServiceUnavailable, "Serviço dependente indisponível"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno")
        };

        if (status >= 500)
            _logger.LogError(ex, "Falha ao processar {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        // 5xx nunca expõe a mensagem da exceção (pode vazar host/infra internos); o detalhe fica só no log.
        var detail = status switch
        {
            StatusCodes.Status503ServiceUnavailable => "Cadastros indisponível no momento. Tente novamente.",
            >= 500 => null,
            _ => ex.Message
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Detail = detail }, ct);
        return true;
    }
}
