using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Web.Middleware;

public sealed class MigrationBlockingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMigrationReadiness _readiness;

    public MigrationBlockingMiddleware(RequestDelegate next, IMigrationReadiness readiness)
    {
        _next = next;
        _readiness = readiness;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_readiness.IsReady && !IsHealthCheck(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            await context.Response.WriteAsync(
                "Service is starting up, please retry shortly.",
                context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private static bool IsHealthCheck(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
