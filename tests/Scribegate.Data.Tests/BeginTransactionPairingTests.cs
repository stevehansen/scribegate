using FluentAssertions;
using Xunit;

namespace Scribegate.Data.Tests;

// Source-level guardrail: every BeginTransactionAsync call site under src/
// must hand the result to ScribegateTransaction.Wrap. The wrapper is what
// keeps the DomainEventSaveChangesInterceptor + post-commit flush in sync
// with explicit transactions; a bare BeginTransactionAsync would silently
// re-introduce the phantom-webhook / audit-orphan bugs RFC #5 fixes.
//
// Implementation is a deliberate string scan, not a Roslyn analyzer — the
// rule is one-line and the cost of a real analyzer (extra project, source
// generators wiring) outweighs the value at one call site.
public class BeginTransactionPairingTests
{
    [Fact]
    public void Every_BeginTransactionAsync_CallSite_Is_Wrapped_By_ScribegateTransaction()
    {
        var srcRoot = LocateSrcRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated migrations and the wrapper's own definition / xmldoc.
            var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (rel.Contains("/Migrations/", StringComparison.Ordinal)) continue;
            if (rel.EndsWith("ScribegateTransaction.cs", StringComparison.Ordinal)) continue;
            if (rel.EndsWith("DomainEventSaveChangesInterceptor.cs", StringComparison.Ordinal)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("BeginTransactionAsync", StringComparison.Ordinal) is false) continue;

                // The expected pattern is a single line that wraps the call:
                //   ScribegateTransaction.Wrap(await db.Database.BeginTransactionAsync(...), scope)
                // Allow the wrapper call to span up to the next 2 lines for
                // formatter-induced wraps.
                var window = string.Concat(lines.Skip(i).Take(3));
                if (window.Contains("ScribegateTransaction.Wrap", StringComparison.Ordinal))
                    continue;

                offenders.Add($"{rel}:{i + 1} → {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "every BeginTransactionAsync call site must be wrapped by ScribegateTransaction.Wrap "
            + "so the domain-event interceptor stays in sync with the explicit transaction");
    }

    private static string LocateSrcRoot()
    {
        // Walk up from the test assembly until we find the repo root (it has a
        // top-level src/ directory).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository's src/ directory from the test assembly path.");
    }
}
