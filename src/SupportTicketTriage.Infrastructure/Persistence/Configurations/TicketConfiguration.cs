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

        builder.Property(t => t.Resolution);
        builder.Property(t => t.ResolvedAt);

        // Stored as its name, like TicketReview.Decision, so reordering the
        // enum can never reinterpret history.
        builder.Property(t => t.ResolutionSource)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Ignore(t => t.IsResolved);

        // A resolution always says where it came from. The domain enforces
        // this through Resolve; the constraint covers the migration backfill,
        // which writes the column without going through the domain, so a row
        // it misses fails the migration instead of persisting unlabelled.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Tickets_ResolutionSource_SetWithResolution",
            "(\"Resolution\" IS NULL) = (\"ResolutionSource\" IS NULL)"));
    }
}
