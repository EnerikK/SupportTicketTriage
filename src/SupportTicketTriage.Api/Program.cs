using Scalar.AspNetCore;
using SupportTicketTriage.Api.Endpoints;
using SupportTicketTriage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Default")!,
    builder.Configuration);

var app = builder.Build();

app.Services.ApplyMigrations();

app.MapHealthChecks("/health");
app.MapTicketEndpoints();

// Deliberately not gated behind IsDevelopment(), which is what the templates
// do. The compose stack runs with no ASPNETCORE_ENVIRONMENT set, so it is
// Production, and gating here would hide the reference in the one place it is
// useful. The honest reason this is safe: the API has no authentication of
// any kind, so an API explorer exposes nothing that curl did not already.
// A real deployment would put both behind auth or drop them entirely.
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();

public partial class Program;
