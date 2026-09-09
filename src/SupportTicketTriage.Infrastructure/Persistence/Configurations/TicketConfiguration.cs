using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Subject)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Body)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();
    }
}
