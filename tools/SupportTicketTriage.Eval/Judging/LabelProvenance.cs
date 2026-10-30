using System.Diagnostics;
using System.Security.Cryptography;

namespace SupportTicketTriage.Eval.Judging;

/// Evidence that the hand labels existed before the verdicts they validate.
///
/// The ordering is the whole guarantee. A label file edited after seeing a
/// judge's verdicts would produce an agreement figure that measures nothing,
/// and nothing in the file itself would show it. So the harness refuses to
/// run against labels with uncommitted changes, and records the commit that
/// last modified them - which git timestamps independently of anything this
/// tool says about itself.
///
/// Shelling out to `git` rather than taking a library dependency: this is a
/// development tool that already assumes a checkout, the three commands below
/// are stable, and a LibGit2Sharp dependency to read two strings would need a
/// DECISIONS entry it could not justify.
public sealed record LabelProvenance(
    string Path,
    string ContentSha256,
    string LastCommit,
    DateTimeOffset LastCommitted)
{
    public static LabelProvenance For(string path)
    {
        var full = System.IO.Path.GetFullPath(path);

        if (!File.Exists(full))
        {
            throw new InvalidOperationException($"Label file not found at {full}.");
        }

        var dirty = Git("status --porcelain -- \"" + full + "\"", full);

        if (!string.IsNullOrWhiteSpace(dirty))
        {
            throw new InvalidOperationException(
                $"'{path}' has uncommitted changes. Commit the labels before running a judge "
                + "configuration against them: the agreement figure is only evidence if the "
                + "labels demonstrably predate the verdicts, and an uncommitted file cannot "
                + "show that. Git reported:\n" + dirty.Trim());
        }

        var commit = Git("log -1 --format=%H -- \"" + full + "\"", full).Trim();

        if (string.IsNullOrWhiteSpace(commit))
        {
            throw new InvalidOperationException(
                $"'{path}' is clean but has never been committed, so there is nothing to show "
                + "the labels predate the verdicts. Commit it first.");
        }

        var when = Git("log -1 --format=%cI -- \"" + full + "\"", full).Trim();

        return new LabelProvenance(
            path,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant(),
            commit,
            DateTimeOffset.Parse(when));
    }

    private static string Git(string arguments, string nearPath)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(nearPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not run git, which is required to evidence that the hand labels predate "
                + "the judge verdicts.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"git {arguments} failed: {error.Trim()}");
    }
}
