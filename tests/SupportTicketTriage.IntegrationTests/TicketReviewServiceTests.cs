using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Generation;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Review;

namespace SupportTicketTriage.IntegrationTests;

/// Drafts are seeded directly rather than generated, because what is under
/// test here is the review decision and what it does to the corpus — not the
/// chat call that produced the text being reviewed.
///
/// Runs against the embedding factory so that the backfill path has a
/// configured deployment to use. With no deployment configured the backfill
/// is skipped entirely, which would make those assertions pass vacuously.
[Collection(nameof(EmbeddingApiCollection))]
public class TicketReviewServiceTests(EmbeddingApiFactory factory)
{
    private const string DraftText = "Both charges relate to a single order, and nothing further is owed.";

    private static Vector VectorOf(params (int Index, float Value)[] components)
    {
        var values = new float[TicketEmbedding.Dimensions];
        foreach (var (index, value) in components)
        {
            values[index] = value;
        }

        return new Vector(values);
    }

    private sealed record Reviewable(Ticket Ticket, TicketDraft Draft, Guid SourceId, string Model);

    /// A ticket carrying a draft that a reviewer could act on: one resolved
    /// source, one draft grounded in it, and an embedding unless the test is
    /// about what happens when ingest failed to produce one.
    private static Reviewable SeedReviewable(
        SupportTicketTriageDbContext db,
        bool citationsValid = true,
        bool withEmbedding = true,
        string? model = null)
    {
        model ??= $"model-{Guid.NewGuid():N}";

        var source = Ticket.Create("Duplicate charge", "Body of the duplicate charge ticket.");
        source.Resolve("The second charge was an authorisation hold and released itself.", ResolutionSource.Imported);
        db.Tickets.Add(source);
        db.TicketEmbeddings.Add(TicketEmbedding.Create(
            source.Id, "redacted duplicate charge body", "v1", model, VectorOf((0, 1f), (1, 0.05f))));

        var ticket = Ticket.Create("Charged twice", "My card was charged twice for one order.");
        db.Tickets.Add(ticket);

        if (withEmbedding)
        {
            db.TicketEmbeddings.Add(TicketEmbedding.Create(
                ticket.Id, "redacted charged twice body", "v1", model, VectorOf((0, 1f))));
        }

        var retrievalSet = new[] { new RetrievedSource(source.Id, 1, 0.91) };
        var cited = citationsValid ? [source.Id] : new[] { Guid.CreateVersion7() };

        var draft = TicketDraft.Create(
            ticket.Id,
            DraftText,
            retrievalSet,
            CitationValidation.Validate(cited, retrievalSet.Select(s => s.TicketId).ToHashSet()),
            "test-chat-model",
            "v1",
            "v1");

        db.TicketDrafts.Add(draft);

        return new Reviewable(ticket, draft, source.Id, model);
    }

    private async Task<(Reviewable Seed, ReviewResult Result)> ReviewAsync(
        ReviewDecision decision,
        string? editedText = null,
        string? rejectionReason = null,
        bool citationsValid = true,
        bool withEmbedding = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();

        var seeded = SeedReviewable(db, citationsValid, withEmbedding);
        await db.SaveChangesAsync();

        factory.Generator.Reset();

        var result = await reviews.SubmitAsync(seeded.Ticket.Id, decision, editedText, rejectionReason);
        return (seeded, result);
    }

    private async Task<Ticket> ReloadAsync(Guid ticketId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        return await db.Tickets.SingleAsync(t => t.Id == ticketId);
    }

    [Fact]
    public async Task ApprovingAsWritten_ResolvesTheTicketWithTheDraftText()
    {
        var (seed, result) = await ReviewAsync(ReviewDecision.Approved);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Equal(ReviewDecision.Approved, result.Review!.Decision);
        Assert.False(result.Review.WasEdited);
        Assert.Equal(seed.Draft.Id, result.Review.TicketDraftId);

        var ticket = await ReloadAsync(seed.Ticket.Id);
        Assert.True(ticket.IsResolved);
        Assert.Equal(DraftText, ticket.Resolution);
        Assert.Equal(ResolutionSource.ApprovedDraft, ticket.ResolutionSource);
    }

    /// The reviewer's text is what survives, and the draft it replaced is
    /// still there — the record of what the model actually produced.
    [Fact]
    public async Task ApprovingAnEditedDraft_StoresTheReviewersTextAndKeepsTheOriginal()
    {
        const string edited = "Both charges belong to one order. We have released the authorisation hold.";

        var (seed, result) = await ReviewAsync(ReviewDecision.Approved, editedText: edited);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.True(result.Review!.WasEdited);
        Assert.Equal(edited, result.Review.FinalText);

        var ticket = await ReloadAsync(seed.Ticket.Id);
        Assert.Equal(edited, ticket.Resolution);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var draft = await db.TicketDrafts.SingleAsync(d => d.Id == seed.Draft.Id);
        Assert.Equal(DraftText, draft.DraftText);
    }

    [Fact]
    public async Task RejectingADraft_LeavesTheTicketUnresolvedAndOutOfTheCorpus()
    {
        var (seed, result) = await ReviewAsync(
            ReviewDecision.Rejected, rejectionReason: "Cites the wrong precedent.");

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Equal("Cites the wrong precedent.", result.Review!.RejectionReason);
        Assert.Null(result.Review.FinalText);

        var ticket = await ReloadAsync(seed.Ticket.Id);
        Assert.False(ticket.IsResolved);
        Assert.Null(ticket.Resolution);
    }

    [Fact]
    public async Task ReviewingTwice_IsRefused()
    {
        var (seed, first) = await ReviewAsync(ReviewDecision.Approved);
        Assert.Equal(ReviewOutcome.Recorded, first.Outcome);

        using var scope = factory.Services.CreateScope();
        var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        var second = await reviews.SubmitAsync(
            seed.Ticket.Id, ReviewDecision.Rejected, null, "Changed my mind.");

        Assert.Equal(ReviewOutcome.AlreadyReviewed, second.Outcome);
        Assert.Equal(1, await db.TicketReviews.CountAsync(r => r.TicketId == seed.Ticket.Id));
    }

    /// A draft that cited evidence it was never shown is withheld from the
    /// reviewer, so it cannot be approved around either.
    [Fact]
    public async Task ADraftWithInvalidCitations_CannotBeReviewed()
    {
        var (seed, result) = await ReviewAsync(ReviewDecision.Approved, citationsValid: false);

        Assert.Equal(ReviewOutcome.NoReviewableDraft, result.Outcome);

        var ticket = await ReloadAsync(seed.Ticket.Id);
        Assert.False(ticket.IsResolved);
    }

    [Fact]
    public async Task ATicketWithNoDraft_CannotBeReviewed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();

        var ticket = Ticket.Create("Nothing drafted", "No draft was ever generated for this one.");
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var result = await reviews.SubmitAsync(ticket.Id, ReviewDecision.Approved, null, null);

        Assert.Equal(ReviewOutcome.NoReviewableDraft, result.Outcome);
    }

    [Fact]
    public async Task AnInvalidSubmission_RecordsNothing()
    {
        var (seed, result) = await ReviewAsync(ReviewDecision.Rejected);

        Assert.Equal(ReviewOutcome.Invalid, result.Outcome);
        Assert.Contains("must record a reason", result.Failure);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        Assert.False(await db.TicketReviews.AnyAsync(r => r.TicketId == seed.Ticket.Id));
    }

    /// The claim in ADR-011: the stored vector is over the ticket's problem
    /// text, so approval has nothing to re-embed.
    ///
    /// Seeded under the configured deployment on purpose. The other tests use
    /// a throwaway model name to keep their vectors out of each other's
    /// retrieval results, but that name is exactly what decides whether a
    /// backfill is needed, so using one here would test the opposite case.
    [Fact]
    public async Task ApprovingATicketThatIsAlreadyEmbedded_CallsTheEmbeddingModelNotAtAll()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();

        var seeded = SeedReviewable(db, model: EmbeddingApiFactory.EmbeddingDeployment);
        await db.SaveChangesAsync();

        factory.Generator.Reset();

        var result = await reviews.SubmitAsync(seeded.Ticket.Id, ReviewDecision.Approved, null, null);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Empty(factory.Generator.Received);
    }

    /// A vector from a different deployment is not a usable vector: ADR-004
    /// filters retrieval by embedding model precisely because vectors from
    /// different models are not comparable. So a ticket carrying only an old
    /// one is, for retrieval purposes, unembedded, and approving it backfills
    /// for the current deployment.
    [Fact]
    public async Task ApprovingATicketEmbeddedUnderAnotherDeployment_BackfillsForTheCurrentOne()
    {
        var (seed, result) = await ReviewAsync(ReviewDecision.Approved);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Single(factory.Generator.Received);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        Assert.True(await db.TicketEmbeddings.AnyAsync(
            e => e.TicketId == seed.Ticket.Id && e.EmbeddingModel == EmbeddingApiFactory.EmbeddingDeployment));
    }

    /// The one case that does need the model: a ticket whose ingest-time
    /// embedding failed would otherwise be resolved and permanently
    /// unretrievable.
    [Fact]
    public async Task ApprovingATicketWithNoEmbedding_BackfillsOne()
    {
        var (seed, result) = await ReviewAsync(ReviewDecision.Approved, withEmbedding: false);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Single(factory.Generator.Received);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        Assert.True(await db.TicketEmbeddings.AnyAsync(e => e.TicketId == seed.Ticket.Id));
    }

    /// The ordering guarantee the specification asks for: the human decision
    /// commits first, and re-indexing is separately recoverable. A reviewer's
    /// approval must not be lost because Azure OpenAI was briefly unreachable.
    [Fact]
    public async Task AFailedBackfill_DoesNotLoseTheApproval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();

        var seeded = SeedReviewable(db, withEmbedding: false);
        await db.SaveChangesAsync();

        factory.Generator.Reset();
        factory.Generator.ShouldThrow = true;

        try
        {
            var result = await reviews.SubmitAsync(seeded.Ticket.Id, ReviewDecision.Approved, null, null);

            Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        }
        finally
        {
            factory.Generator.Reset();
        }

        var ticket = await ReloadAsync(seeded.Ticket.Id);
        Assert.True(ticket.IsResolved);
        Assert.Equal(DraftText, ticket.Resolution);

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        Assert.True(await verifyDb.TicketReviews.AnyAsync(r => r.TicketId == seeded.Ticket.Id));

        // The embedding is what is missing, and that is the recoverable half.
        Assert.False(await verifyDb.TicketEmbeddings.AnyAsync(e => e.TicketId == seeded.Ticket.Id));
    }

    /// The feedback loop, end to end: approval is what moves a ticket into the
    /// retrieval corpus, because the corpus is defined as resolved tickets.
    [Fact]
    public async Task AnApprovedTicket_BecomesRetrievableAsASource()
    {
        var model = $"model-{Guid.NewGuid():N}";
        Guid askerId;
        Guid approvedId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
            var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();

            var seeded = SeedReviewable(db, model: model);
            approvedId = seeded.Ticket.Id;

            // A later ticket asking about the same thing, on the same model.
            var asker = Ticket.Create("Double charge again", "I was charged twice as well.");
            db.Tickets.Add(asker);
            db.TicketEmbeddings.Add(TicketEmbedding.Create(
                asker.Id, "redacted double charge again", "v1", model, VectorOf((0, 1f))));
            askerId = asker.Id;

            await db.SaveChangesAsync();

            var before = await retrieval.FindSimilarAsync(askerId);
            Assert.DoesNotContain(before, s => s.TicketId == approvedId);

            var result = await reviews.SubmitAsync(approvedId, ReviewDecision.Approved, null, null);
            Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        }

        using var after = factory.Services.CreateScope();
        var afterRetrieval = after.ServiceProvider.GetRequiredService<TicketRetrievalService>();

        var similar = await afterRetrieval.FindSimilarAsync(askerId);

        var found = Assert.Single(similar, s => s.TicketId == approvedId);
        Assert.Equal(DraftText, found.Resolution);
    }

    [Fact]
    public async Task PostReview_RecordsTheDecisionAndServesItBack()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            ticketId = SeedReviewable(db).Ticket.Id;
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();

        var posted = await client.PostAsJsonAsync(
            $"/tickets/{ticketId}/review",
            new SubmitReviewRequest("approved", null, null));

        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        var fetched = await client.GetFromJsonAsync<TicketReviewResponse>($"/tickets/{ticketId}/review");

        Assert.Equal("Approved", fetched!.Decision);
        Assert.Equal(DraftText, fetched.FinalText);
        Assert.False(fetched.WasEdited);
    }

    [Fact]
    public async Task PostReview_RejectsADecisionOutsideTheClosedSet()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            ticketId = SeedReviewable(db).Ticket.Id;
            await db.SaveChangesAsync();
        }

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/tickets/{ticketId}/review",
            new SubmitReviewRequest("Escalated", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostReview_IsNotFoundForAnUnknownTicket()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/tickets/{Guid.CreateVersion7()}/review",
            new SubmitReviewRequest("Approved", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReview_IsNotFoundBeforeATicketIsReviewed()
    {
        var response = await factory.CreateClient().GetAsync($"/tickets/{Guid.CreateVersion7()}/review");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private const string ManualReply = "Refunded the duplicate charge; it will show within five days.";

    /// A ticket a routing gate refused: no draft was ever generated for it.
    private static Ticket SeedUndrafted(
        SupportTicketTriageDbContext db,
        bool withEmbedding = true,
        string? model = null)
    {
        model ??= $"model-{Guid.NewGuid():N}";

        var ticket = Ticket.Create("Unrecognised charge", "There is a charge on my card I do not recognise.");
        db.Tickets.Add(ticket);

        if (withEmbedding)
        {
            db.TicketEmbeddings.Add(TicketEmbedding.Create(
                ticket.Id, "redacted unrecognised charge body", "v1", model, VectorOf((0, 1f))));
        }

        return ticket;
    }

    private async Task<T> SeedAsync<T>(Func<SupportTicketTriageDbContext, T> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        var seeded = seed(db);
        await db.SaveChangesAsync();
        return seeded;
    }

    private async Task<ReviewResult> ResolveManuallyAsync(Guid ticketId, string? reply = ManualReply)
    {
        using var scope = factory.Services.CreateScope();
        var reviews = scope.ServiceProvider.GetRequiredService<TicketReviewService>();

        factory.Generator.Reset();

        return await reviews.SubmitAsync(ticketId, ReviewDecision.ResolvedManually, reply, null);
    }

    [Fact]
    public async Task ResolvingManually_ATicketWithNoDraft_ResolvesItWithTheReply()
    {
        var ticket = await SeedAsync(db => SeedUndrafted(db));

        var result = await ResolveManuallyAsync(ticket.Id, $"  {ManualReply}\n");

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Equal(ReviewDecision.ResolvedManually, result.Review!.Decision);
        Assert.Null(result.Review.TicketDraftId);
        Assert.Equal(ManualReply, result.Review.FinalText);

        var reloaded = await ReloadAsync(ticket.Id);
        Assert.Equal(ManualReply, reloaded.Resolution);
        Assert.Equal(ResolutionSource.ManualReply, reloaded.ResolutionSource);
    }

    /// The withheld draft stays linked as the record of what the model got
    /// wrong, and its text never becomes the resolution.
    [Fact]
    public async Task ResolvingManually_ATicketWithAWithheldDraft_LinksTheDraftButNotItsText()
    {
        var seed = await SeedAsync(db => SeedReviewable(db, citationsValid: false));

        var result = await ResolveManuallyAsync(seed.Ticket.Id);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Equal(seed.Draft.Id, result.Review!.TicketDraftId);

        var reloaded = await ReloadAsync(seed.Ticket.Id);
        Assert.Equal(ManualReply, reloaded.Resolution);
        Assert.NotEqual(DraftText, reloaded.Resolution);
    }

    [Fact]
    public async Task ResolvingManually_IsRefusedWhileAReviewableDraftExists()
    {
        var seed = await SeedAsync(db => SeedReviewable(db));

        var result = await ResolveManuallyAsync(seed.Ticket.Id);

        Assert.Equal(ReviewOutcome.ReviewableDraftExists, result.Outcome);
        Assert.False((await ReloadAsync(seed.Ticket.Id)).IsResolved);
    }

    /// An imported corpus ticket has no draft, so it reaches the manual path;
    /// without this refusal Ticket.Resolve would throw and the API would 500.
    [Fact]
    public async Task ResolvingManually_IsRefusedForAnAlreadyResolvedTicket()
    {
        var ticket = await SeedAsync(db =>
        {
            var imported = SeedUndrafted(db);
            imported.Resolve("The imported resolution.", ResolutionSource.Imported);
            return imported;
        });

        var result = await ResolveManuallyAsync(ticket.Id);

        Assert.Equal(ReviewOutcome.AlreadyResolved, result.Outcome);

        var reloaded = await ReloadAsync(ticket.Id);
        Assert.Equal("The imported resolution.", reloaded.Resolution);
        Assert.Equal(ResolutionSource.Imported, reloaded.ResolutionSource);
    }

    [Fact]
    public async Task ResolvingManually_ATicketWithNoEmbedding_BackfillsOne()
    {
        var ticket = await SeedAsync(db => SeedUndrafted(db, withEmbedding: false));

        var result = await ResolveManuallyAsync(ticket.Id);

        Assert.Equal(ReviewOutcome.Recorded, result.Outcome);
        Assert.Single(factory.Generator.Received);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        Assert.True(await db.TicketEmbeddings.AnyAsync(e => e.TicketId == ticket.Id));
    }

    /// The gap this decision closes: a ticket the system could not draft for
    /// becomes the precedent the next similar ticket needs.
    [Fact]
    public async Task AManuallyResolvedTicket_BecomesRetrievableAsASource()
    {
        var model = $"model-{Guid.NewGuid():N}";

        var (resolvedId, askerId) = await SeedAsync(db =>
        {
            var gated = SeedUndrafted(db, model: model);

            var asker = Ticket.Create("Charge I do not know", "A charge appeared that I cannot place.");
            db.Tickets.Add(asker);
            db.TicketEmbeddings.Add(TicketEmbedding.Create(
                asker.Id, "redacted charge i do not know", "v1", model, VectorOf((0, 1f))));

            return (gated.Id, asker.Id);
        });

        using (var scope = factory.Services.CreateScope())
        {
            var before = await scope.ServiceProvider.GetRequiredService<TicketRetrievalService>().FindSimilarAsync(askerId);
            Assert.DoesNotContain(before, s => s.TicketId == resolvedId);
        }

        Assert.Equal(ReviewOutcome.Recorded, (await ResolveManuallyAsync(resolvedId)).Outcome);

        using var after = factory.Services.CreateScope();
        var similar = await after.ServiceProvider.GetRequiredService<TicketRetrievalService>().FindSimilarAsync(askerId);

        var found = Assert.Single(similar, s => s.TicketId == resolvedId);
        Assert.Equal(ManualReply, found.Resolution);
    }

    /// The check constraint, not only the service: an approval or rejection
    /// that names no draft cannot be stored by any code path.
    [Fact]
    public async Task TheDatabase_RefusesAnApprovalThatNamesNoDraft()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        var ticket = SeedUndrafted(db);
        await db.SaveChangesAsync();

        Assert.True(ReviewSubmission.TryCreate(ReviewDecision.Approved, "Text.", null, null, out var approval, out _));
        db.TicketReviews.Add(TicketReview.Create(ticket.Id, draftId: null, approval!));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("CK_TicketReviews_DraftRequiredUnlessResolvedManually", postgres.ConstraintName);
    }

    [Fact]
    public async Task PostReview_ResolvedManually_IsRecordedWithNoDraft()
    {
        var ticket = await SeedAsync(db => SeedUndrafted(db));

        var client = factory.CreateClient();

        var posted = await client.PostAsJsonAsync(
            $"/tickets/{ticket.Id}/review",
            new SubmitReviewRequest("resolvedmanually", ManualReply, null));

        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        var fetched = await client.GetFromJsonAsync<TicketReviewResponse>($"/tickets/{ticket.Id}/review");

        Assert.Equal("ResolvedManually", fetched!.Decision);
        Assert.Equal(ManualReply, fetched.FinalText);
        Assert.Null(fetched.DraftId);
    }
}
