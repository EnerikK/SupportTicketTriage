using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence;

public sealed class SupportTicketTriageDbContext(DbContextOptions<SupportTicketTriageDbContext> options)
    : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketEmbedding> TicketEmbeddings => Set<TicketEmbedding>();
    public DbSet<TicketClassification> TicketClassifications => Set<TicketClassification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportTicketTriageDbContext).Assembly);
    }
}
