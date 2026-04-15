using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Scribegate.Web.Api;

public static class DiffService
{
    public static DiffResult ComputeDiff(string? oldContent, string? newContent)
    {
        var differ = new Differ();
        var builder = new InlineDiffBuilder(differ);
        var diff = builder.BuildDiffModel(oldContent ?? "", newContent ?? "");

        var lines = diff.Lines.Select(line => new DiffLine
        {
            Type = line.Type switch
            {
                ChangeType.Inserted => "added",
                ChangeType.Deleted => "removed",
                ChangeType.Modified => "modified",
                ChangeType.Imaginary => "imaginary",
                _ => "unchanged",
            },
            Text = line.Text,
            Position = line.Position,
        }).ToList();

        return new DiffResult
        {
            Lines = lines,
            HasChanges = diff.HasDifferences,
        };
    }
}

public class DiffResult
{
    public required List<DiffLine> Lines { get; init; }
    public bool HasChanges { get; init; }
}

public class DiffLine
{
    public required string Type { get; init; }
    public required string Text { get; init; }
    public int? Position { get; init; }
}
