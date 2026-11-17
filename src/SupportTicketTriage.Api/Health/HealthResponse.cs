using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SupportTicketTriage.Api.Health;

/// The default health response is the bare word "Degraded", which says that
/// something is wrong but not what. This writes each check's description and
/// data as JSON, so the reason a deployment is unavailable is one request away.
public static class HealthResponse
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    data = entry.Value.Data,
                }),
        });
}
