using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence;

public sealed class SupportTicketTriageDbContext(DbContextOptions<SupportTicketTriageDbContext> options)
    : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportTicketTriageDbContext).Assembly);
    }
}
