using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

/// Migrations that move or constrain existing data, tested against rows that
/// exist before the migration runs.
///
/// That rules out the shared database, which the API migrates to the latest
/// version at startup. Each test creates its own database in the same
/// container, migrates to a named migration, seeds with raw SQL (the current
/// EF model can be ahead of the schema at that point), then migrates to
/// another named one. Pinning both ends means a later migration cannot break
/// these tests for a reason unrelated to the migration under test.
///
/// The AddResolutionSource backfill runs once, against real data. Its check
/// constraint proves every resolved ticket got a source, but not that it got
/// the right one; a wrong label would persist silently.
[Collection(nameof(TicketApiCollection))]
public class MigrationTests(TicketApiFactory factory)
{
    private const string MigrationBeforeBackfill = "AddDraftResolvedModel";
    private const string BackfillMigration = "AddResolutionSource";
    private const string ManualResolutionMigration = "AllowManualResolution";

    private sealed class SourceRow
    {
        public Guid Id { get; init; }
        public string? ResolutionSource { get; init; }
    }

    [Fact]
    public Task Backfill_LabelsAResolutionByWhetherAnApprovedReviewProducedIt() =>
        InThrowawayDatabaseAsync(async (db, migrator) =>
        {
            await migrator.MigrateAsync(MigrationBeforeBackfill);

            var approved = Guid.NewGuid();
            var imported = Guid.NewGuid();
            var importedThenRejected = Guid.NewGuid();
            var unresolvedRejected = Guid.NewGuid();

            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Tickets" ("Id", "Subject", "Body", "CreatedAt", "Resolution", "ResolvedAt") VALUES
                    ({approved}, 'subject', 'body', now(), 'The reviewer approved this.', now()),
                    ({imported}, 'subject', 'body', now(), 'Imported into the corpus.', now()),
                    ({importedThenRejected}, 'subject', 'body', now(), 'Imported, then a draft was rejected.', now()),
                    ({unresolvedRejected}, 'subject', 'body', now(), NULL, NULL)
                """);

            var approvedDraft = Guid.NewGuid();
            var importedDraft = Guid.NewGuid();
            var unresolvedDraft = Guid.NewGuid();

            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "TicketDrafts" ("Id", "TicketId", "DraftText", "CitationsValid", "InvalidCitationCount",
                                            "ChatModel", "PromptVersion", "RedactionVersion", "CreatedAt") VALUES
                    ({approvedDraft}, {approved}, 'draft', true, 0, 'chat', 'p', 'r', now()),
                    ({importedDraft}, {importedThenRejected}, 'draft', true, 0, 'chat', 'p', 'r', now()),
                    ({unresolvedDraft}, {unresolvedRejected}, 'draft', true, 0, 'chat', 'p', 'r', now())
                """);

            // A rejection on a resolved ticket is the case that separates
            // "has an approved review" from "has any review".
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "TicketReviews" ("Id", "TicketId", "TicketDraftId", "Decision", "FinalText",
                                             "WasEdited", "RejectionReason", "CreatedAt") VALUES
                    ({Guid.NewGuid()}, {approved}, {approvedDraft}, 'Approved', 'The reviewer approved this.', false, NULL, now()),
                    ({Guid.NewGuid()}, {importedThenRejected}, {importedDraft}, 'Rejected', NULL, false, 'Wrong precedent.', now()),
                    ({Guid.NewGuid()}, {unresolvedRejected}, {unresolvedDraft}, 'Rejected', NULL, false, 'Wrong precedent.', now())
                """);

            await migrator.MigrateAsync(BackfillMigration);

            var sources = await db.Database
                .SqlQuery<SourceRow>($"""SELECT "Id", "ResolutionSource" FROM "Tickets" """)
                .ToDictionaryAsync(r => r.Id, r => r.ResolutionSource);

            Assert.Equal("ApprovedDraft", sources[approved]);
            Assert.Equal("Imported", sources[imported]);
            Assert.Equal("Imported", sources[importedThenRejected]);
            Assert.Null(sources[unresolvedRejected]);
        });

    /// Down drops the check before the column it references, and the
    /// migration can be applied again afterwards, backfill included.
    [Fact]
    public Task Down_RemovesTheColumnAndTheCheck_AndUpReappliesThem() =>
        InThrowawayDatabaseAsync(async (db, migrator) =>
        {
            await migrator.MigrateAsync(MigrationBeforeBackfill);

            var resolved = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Tickets" ("Id", "Subject", "Body", "CreatedAt", "Resolution", "ResolvedAt")
                VALUES ({resolved}, 'subject', 'body', now(), 'Imported into the corpus.', now())
                """);

            await migrator.MigrateAsync(BackfillMigration);
            await migrator.MigrateAsync(MigrationBeforeBackfill);

            Assert.Equal(0, await CountAsync(db, $"""
                SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
                WHERE table_name = 'Tickets' AND column_name = 'ResolutionSource'
                """));
            Assert.Equal(0, await CountAsync(db, $"""
                SELECT COUNT(*)::int AS "Value" FROM pg_constraint
                WHERE conname = 'CK_Tickets_ResolutionSource_SetWithResolution'
                """));

            await migrator.MigrateAsync(BackfillMigration);

            var source = await db.Database
                .SqlQuery<string?>($"""SELECT "ResolutionSource" AS "Value" FROM "Tickets" WHERE "Id" = {resolved}""")
                .SingleAsync();
            Assert.Equal("Imported", source);
        });

    /// The old schema cannot represent a manual resolution with no draft, so
    /// rolling back must refuse while one exists rather than delete it or
    /// point it at a draft that does not exist. The refusal must also leave
    /// the schema as it was, which holds because PostgreSQL runs the migration's
    /// DDL inside a transaction.
    [Fact]
    public Task AllowManualResolutionDown_RefusesWhileADraftlessManualResolutionExists() =>
        InThrowawayDatabaseAsync(async (db, migrator) =>
        {
            await migrator.MigrateAsync(ManualResolutionMigration);

            var ticket = Guid.NewGuid();
            var review = Guid.NewGuid();

            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Tickets" ("Id", "Subject", "Body", "CreatedAt", "Resolution", "ResolvedAt", "ResolutionSource")
                VALUES ({ticket}, 'subject', 'body', now(), 'Written by hand.', now(), 'ManualReply')
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "TicketReviews" ("Id", "TicketId", "TicketDraftId", "Decision", "FinalText",
                                             "WasEdited", "RejectionReason", "CreatedAt")
                VALUES ({review}, {ticket}, NULL, 'ResolvedManually', 'Written by hand.', false, NULL, now())
                """);

            var refused = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(BackfillMigration));
            Assert.Equal(PostgresErrorCodes.NotNullViolation, refused.SqlState);

            Assert.Equal(1, await CountAsync(db, $"""
                SELECT COUNT(*)::int AS "Value" FROM "TicketReviews" WHERE "Id" = {review}
                """));
            Assert.Equal("YES", await ColumnNullabilityAsync(db));

            await db.Database.ExecuteSqlAsync($"""DELETE FROM "TicketReviews" WHERE "Id" = {review}""");
            await migrator.MigrateAsync(BackfillMigration);

            Assert.Equal("NO", await ColumnNullabilityAsync(db));
            Assert.Equal(0, await CountAsync(db, $"""
                SELECT COUNT(*)::int AS "Value" FROM pg_constraint
                WHERE conname = 'CK_TicketReviews_DraftRequiredUnlessResolvedManually'
                """));
        });

    private static Task<string> ColumnNullabilityAsync(SupportTicketTriageDbContext db) =>
        db.Database.SqlQuery<string>($"""
            SELECT is_nullable::text AS "Value" FROM information_schema.columns
            WHERE table_name = 'TicketReviews' AND column_name = 'TicketDraftId'
            """).SingleAsync();

    private static Task<int> CountAsync(SupportTicketTriageDbContext db, FormattableString sql) =>
        db.Database.SqlQuery<int>(sql).SingleAsync();

    /// Runs the body against a database of its own, uniquely named so that
    /// parallel runs against the same container cannot collide, and drops it
    /// afterwards whether the body passed or not.
    private async Task InThrowawayDatabaseAsync(Func<SupportTicketTriageDbContext, IMigrator, Task> body)
    {
        await using var db = CreateThrowawayDatabaseContext();

        try
        {
            await body(db, db.GetService<IMigrator>());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private SupportTicketTriageDbContext CreateThrowawayDatabaseContext()
    {
        // Read from configuration, not from the shared DbContext: Npgsql strips
        // the password from a connection string once it has been opened.
        var sharedConnection = factory.Services
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("Default")!;

        var connection = new NpgsqlConnectionStringBuilder(sharedConnection)
        {
            Database = $"backfill_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<SupportTicketTriageDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector())
            .Options;

        return new SupportTicketTriageDbContext(options);
    }
}
