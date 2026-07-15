namespace AutoTrainer.Api.Endpoints;

// Turns a client disconnect into a logged 499 instead of an exception escaping into framework code.
//
// A minimal API handler's CancellationToken parameter binds to HttpContext.RequestAborted, so anything the
// handler passes it to (EF queries, DbContext creation) throws OperationCanceledException when the caller
// hangs up or when the host aborts in-flight requests during shutdown. Neither is a fault, but letting it
// propagate makes the debugger break on it as user-unhandled.
//
// Only an abort of THIS request is swallowed: any other OperationCanceledException is a real bug and is
// rethrown. 499 (Client Closed Request) is nginx's convention for "the caller went away"; nothing reads it,
// since by definition there is no one left on the connection.
internal static class ClientDisconnect
{
    public static T LogClientDisconnects<T>(this T builder)
        where T : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            try
            {
                return await next(ctx);
            }
            catch (OperationCanceledException) when (ctx.HttpContext.RequestAborted.IsCancellationRequested)
            {
                var request = ctx.HttpContext.Request;

                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AutoTrainer.Api.Requests")
                    .LogInformation("Request aborted by client: {Method} {Path}{Query}",
                        request.Method, request.Path, request.QueryString);

                return Results.StatusCode(499);
            }
        });

        return builder;
    }
}
