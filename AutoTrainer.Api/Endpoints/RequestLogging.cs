namespace AutoTrainer.Api.Endpoints;

// Dev-only per-endpoint request logging. Attaching this to a route group (or a single endpoint) logs one line
// per matched request — method, path, and query string — under the "AutoTrainer.Api.Requests" category.
//
// It is gated on the Development environment AT REGISTRATION TIME: outside Development the endpoint filter is
// never added to the pipeline, so there is no runtime cost or behavior change in production. Because it runs
// after the endpoint is matched, it only sees our endpoints (not /health or the SignalR hub) and does not fire
// for unmatched routes or requests that fail parameter binding before reaching the handler.
internal static class RequestLogging
{
    public static T LogRequestsInDevelopment<T>(this T builder, IEndpointRouteBuilder app)
        where T : IEndpointConventionBuilder
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return builder;

        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var request = ctx.HttpContext.Request;

            ctx.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AutoTrainer.Api.Requests")
                .LogInformation("Request {Method} {Path}{Query}", request.Method, request.Path, request.QueryString);

            return await next(ctx);
        });

        return builder;
    }
}
