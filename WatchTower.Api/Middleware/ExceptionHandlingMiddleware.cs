using System.Diagnostics;
using WatchTower.Application.Exceptions;

namespace WatchTower.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemDetailsAsync(context, ex);
        }
    }

    private async Task WriteProblemDetailsAsync(HttpContext context, Exception exception)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation error"),
            DuplicateEmailException => (StatusCodes.Status409Conflict, "Conflict"),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            EmailDeliveryException => (StatusCodes.Status502BadGateway, "Email delivery failed"),
            _ => (StatusCodes.Status500InternalServerError, "Server error")
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception");
        else
            logger.LogWarning(exception, "Request failed: {Title}", title);

        context.Response.StatusCode = status;

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var problem = new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : exception.Message,
            traceId
        };

        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
