namespace OficinaOs.Application.Exceptions;

/// <summary>Recurso inexistente → HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Operação proibida pela regra de negócio → HTTP 403 (ex.: cliente inativo).</summary>
public class ForbiddenOperationException : Exception
{
    public ForbiddenOperationException(string message) : base(message) { }
}

/// <summary>Dados da requisição incoerentes → HTTP 400.</summary>
public class InvalidRequestException : Exception
{
    public InvalidRequestException(string message) : base(message) { }
}
