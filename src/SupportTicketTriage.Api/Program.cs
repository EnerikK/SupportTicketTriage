using SupportTicketTriage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default")!);

var app = builder.Build();

app.Services.ApplyMigrations();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
