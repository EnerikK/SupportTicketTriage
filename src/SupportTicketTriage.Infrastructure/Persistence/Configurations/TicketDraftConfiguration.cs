using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence.Configurations;

public sealed class TicketDraftConfiguration : IEntityTypeConfiguration<TicketDraft>
{
    public void Configure(EntityTypeBuilder<TicketDraft> builder)
    {
        builder.ToTable("TicketDrafts");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.DraftText).IsRequired();
        builder.Property(d => d.CitationsValid).IsRequired();
        builder.Property(d => d.InvalidCitationCount).IsRequired();
        builder.Property(d => d.ChatModel).HasMaxLength(200).IsRequired();
        builder.Property(d => d.PromptVersion).HasMaxLength(50).IsRequired();
        builder.Property(d => d.RedactionVersion).HasMaxLength(50).IsRequired();
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.HasIndex(d => new { d.TicketId, d.CreatedAt }).IsDescending(false, true);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(d => d.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Sources)
            .WithOne()
            .HasForeignKey(s => s.DraftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Sources).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class TicketDraftSourceConfiguration : IEntityTypeConfiguration<TicketDraftSource>
{
    public void Configure(EntityTypeBuilder<TicketDraftSource> builder)
    {
        builder.ToTable("TicketDraftSources");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Rank).IsRequired();
        builder.Property(s => s.Similarity).IsRequired();
        builder.Property(s => s.WasCited).IsRequired();

        // A draft cites a source at most once, and the retrieval set it was
        // built from cannot contain the same ticket twice.
        builder.HasIndex(s => new { s.DraftId, s.SourceTicketId }).IsUnique();

        // No cascade from the source ticket: deleting a historical ticket must
        // not silently rewrite the evidence an existing draft was grounded on.
        // Restrict makes that an explicit decision rather than a side effect.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(s => s.SourceTicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
