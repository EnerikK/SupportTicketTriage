using Microsoft.Extensions.Logging;

namespace SupportTicketTriage.IntegrationTests;

/// Captures the SQL EF Core actually sends, so tests can prove that filtering
/// and limiting happen in the database rather than in memory.
public sealed class SqlCapturingLoggerProvider : ILoggerProvider
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return _commands.ToList();
            }
        }
    }

    public void Clear()
    {
        lock (_commands)
        {
            _commands.Clear();
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        categoryName == CommandCategory ? new CapturingLogger(this) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_commands)
        {
            _commands.Add(message);
        }
    }

    private sealed class CapturingLogger(SqlCapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => owner.Add(formatter(state, exception));
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
