using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

/// A database of its own in the shared PostgreSQL container.
///
/// For tests that need rows to exist before a migration runs, or exact counts
/// that other tests' tickets would disturb; the shared database is migrated
/// at startup and holds everything every other test wrote. Uniquely named so
/// that parallel runs cannot collide, and dropped on disposal.
internal sealed class ThrowawayDatabase : IAsyncDisposable
{
    private readonly DbContextOptions<SupportTicketTriageDbContext> _options;
    private readonly ILoggerFactory? _loggerFactory;

    private ThrowawayDatabase(DbContextOptions<SupportTicketTriageDbContext> options, ILoggerFactory? loggerFactory)
    {
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <param name="sessionTimeZone">The session's time zone, for a test that
    /// must show a query does not depend on it.</param>
    /// <param name="sql">Receives the SQL sent, for a test that counts statements.</param>
    public static ThrowawayDatabase Create(
        TicketApiFactory factory,
        string? sessionTimeZone = null,
        ILoggerProvider? sql = null)
    {
        // Read from configuration, not from the shared DbContext: Npgsql strips
        // the password from a connection string once it has been opened.
        var shared = factory.Services
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("Default")!;

        var connection = new NpgsqlConnectionStringBuilder(shared)
        {
            Database = $"throwaway_{Guid.NewGuid():N}",
            Timezone = sessionTimeZone,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<SupportTicketTriageDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.UseVector());

        var loggerFactory = sql is null ? null : LoggerFactory.Create(logging => logging.AddProvider(sql));
        if (loggerFactory is not null)
        {
            options.UseLoggerFactory(loggerFactory);
        }

        return new ThrowawayDatabase(options.Options, loggerFactory);
    }

    public SupportTicketTriageDbContext CreateContext() => new(_options);

    public async ValueTask DisposeAsync()
    {
        await using (var db = CreateContext())
        {
            await db.Database.EnsureDeletedAsync();
        }

        _loggerFactory?.Dispose();
    }
}
