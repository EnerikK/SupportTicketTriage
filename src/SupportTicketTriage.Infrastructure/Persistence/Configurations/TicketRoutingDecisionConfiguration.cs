using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence.Configurations;

public sealed class TicketRoutingDecisionConfiguration : IEntityTypeConfiguration<TicketRoutingDecision>
{
    public void Configure(EntityTypeBuilder<TicketRoutingDecision> builder)
    {
        builder.ToTable("TicketRoutingDecisions");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.IsDraftEligible).IsRequired();

        // The flags enum as text, so a row reads as "ClassificationScore,
        // RetrievalSimilarity" in psql rather than as 3. Which gate failed is
        // the whole point of the record, and an ordinal hides it.
        builder.Property(d => d.FailedGates).HasConversion<string>().HasMaxLength(100).IsRequired();

        // Nullable by design: absent and low are different failures.
        builder.Property(d => d.ClassificationScore);
        builder.Property(d => d.TopSimilarity);

        builder.Property(d => d.MinimumClassificationScore).IsRequired();
        builder.Property(d => d.MinimumSimilarity).IsRequired();
        builder.Property(d => d.CreatedAt).IsRequired();

        // Same shape as TicketClassifications, for the same reason: a second
        // decision for one ticket is a re-evaluation, not a conflict.
        builder.HasIndex(d => new { d.TicketId, d.CreatedAt }).IsDescending(false, true);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(d => d.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
