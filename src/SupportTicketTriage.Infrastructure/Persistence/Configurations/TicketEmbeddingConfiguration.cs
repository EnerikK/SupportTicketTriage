using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence.Configurations;

public sealed class TicketEmbeddingConfiguration : IEntityTypeConfiguration<TicketEmbedding>
{
    public void Configure(EntityTypeBuilder<TicketEmbedding> builder)
    {
        builder.ToTable("TicketEmbeddings");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.RedactedText).IsRequired();
        builder.Property(e => e.RedactionVersion).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EmbeddingModel).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.Property(e => e.Embedding)
            .HasColumnType($"vector({TicketEmbedding.Dimensions})")
            .IsRequired();

        // Re-embedding under a new deployment adds a row rather than replacing
        // one, so vectors from different models are never silently compared.
        builder.HasIndex(e => new { e.TicketId, e.EmbeddingModel }).IsUnique();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
