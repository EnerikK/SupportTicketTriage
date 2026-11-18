using Microsoft.AspNetCore.Http.HttpResults;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Dashboard;
using SupportTicketTriage.Infrastructure.Dashboard;

namespace SupportTicketTriage.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/dashboard", GetDashboard);
    }

    /// Read-only pipeline health over 7, 30 or 90 whole UTC days (ADR-046).
    /// Nothing here writes, and nothing here sends.
    private static async Task<Results<Ok<DashboardResponse>, ValidationProblem>> GetDashboard(
        DashboardService dashboard,
        CancellationToken ct,
        string range = "30d")
    {
        if (!DashboardPeriod.TryCreate(range, DateTimeOffset.UtcNow, out var period))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["range"] = ["Range must be one of 7d, 30d or 90d."],
            });
        }

        var snapshot = await dashboard.GetAsync(period!, ct);
        return TypedResults.Ok(DashboardResponse.Create(period!, snapshot));
    }
}
