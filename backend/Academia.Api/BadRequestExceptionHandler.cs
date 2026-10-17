using Microsoft.AspNetCore.Diagnostics;

/// <summary>
/// Pedido que não se consegue ler (JSON malformado, corpo grande demais…) é erro do cliente, não 500.
/// Em Development o .NET lança BadHttpRequestException (RouteHandlerOptions.ThrowOnBadRequest) e, sem isto,
/// o UseExceptionHandler transformava-a num 500; fora de Development já responde 400, mas sem corpo.
/// </summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest) return false;
        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = badRequest.StatusCode,
                Title = "Pedido inválido.",
                Detail = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "O pedido é demasiado grande."
                    : "O corpo do pedido não é JSON válido ou não tem o formato esperado."
            }
        });
    }
}
