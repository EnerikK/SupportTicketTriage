using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence.Configurations;

public sealed class TicketReviewConfiguration : IEntityTypeConfiguration<TicketReview>
{
    public void Configure(EntityTypeBuilder<TicketReview> builder)
    {
        builder.ToTable("TicketReviews");

        builder.HasKey(r => r.Id);

        // Stored as its name rather than its number: a review decision is read
        // by a human looking at the table far more often than it is read by
        // code, and renumbering the enum must never silently reinterpret
        // history.
        builder.Property(r => r.Decision)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.WasEdited).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.RejectionReason).HasMaxLength(2000);

        // One review per ticket. The service checks first and returns a
        // conflict, but the index is what actually guarantees it: two
        // concurrent approvals would otherwise both pass the check and the
        // second would try to resolve an already-resolved ticket.
        builder.HasIndex(r => r.TicketId).IsUnique();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(r => r.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // No cascade from the draft: the reviewed draft is the evidence for
        // this decision, and deleting it must not quietly take the human
        // decision with it.
        //
        // NoAction rather than Restrict, which is the stricter-looking choice
        // and the wrong one here. Both rows hang off the same ticket, so
        // deleting that ticket cascades to both in one statement. PostgreSQL
        // checks RESTRICT immediately, so it would fire if the draft happened
        // to be removed before the review; NO ACTION is checked at the end of
        // the statement, by which point both are gone and the reference is
        // satisfied. Nothing deletes a draft on its own — no such path exists.
        builder.HasOne<TicketDraft>()
            .WithMany()
            .HasForeignKey(r => r.TicketDraftId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
