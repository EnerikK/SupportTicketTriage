using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence.Configurations;

public sealed class TicketClassificationConfiguration : IEntityTypeConfiguration<TicketClassification>
{
    public void Configure(EntityTypeBuilder<TicketClassification> builder)
    {
        builder.ToTable("TicketClassifications");

        builder.HasKey(c => c.Id);

        // Stored as text rather than an ordinal so the table is readable in
        // psql and so inserting a member in the middle of the enum later
        // cannot silently reinterpret existing rows.
        builder.Property(c => c.Category).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(c => c.SelfReportedScore).IsRequired();
        builder.Property(c => c.RedactedText).IsRequired();
        builder.Property(c => c.RedactionVersion).HasMaxLength(50).IsRequired();
        builder.Property(c => c.ChatModel).HasMaxLength(200).IsRequired();
        builder.Property(c => c.PromptVersion).HasMaxLength(50).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // Deliberately not unique on (TicketId, ChatModel), unlike
        // TicketEmbeddings. That constraint exists because vectors from
        // different deployments are incomparable and mixing them is silently
        // wrong. Two classifications of one ticket are merely redundant, so
        // the index serves "find the most recent" instead.
        builder.HasIndex(c => new { c.TicketId, c.CreatedAt }).IsDescending(false, true);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
