using System.CommandLine;

namespace Scribegate.Cli.Commands;

public static class DocCommands
{
    public static Command Create()
    {
        var cmd = new Command("doc", "Document management");

        var listCmd = new Command("list", "List documents in a repository");
        var repoSlugArg = new Argument<string>("repo", "Repository slug");
        listCmd.AddArgument(repoSlugArg);
        listCmd.SetHandler(async (repoSlug) =>
        {
            var client = new ApiClient();
            var result = await client.GetAsync<DocListResponse>($"/api/v1/repositories/{repoSlug}/documents");
            OutputFormatter.PrintTable(
                ["Path", "Created By", "Created At"],
                result!.Items.Select(d => new[] { d.Path, d.CreatedBy, d.CreatedAt }));
        }, repoSlugArg);

        var viewCmd = new Command("view", "View a document (rendered as markdown source)");
        var viewRepoArg = new Argument<string>("repo", "Repository slug");
        var viewPathArg = new Argument<string>("path", "Document path");
        viewCmd.AddArgument(viewRepoArg);
        viewCmd.AddArgument(viewPathArg);
        viewCmd.SetHandler(async (repo, path) =>
        {
            var client = new ApiClient();
            var result = await client.GetAsync<DocResponse>($"/api/v1/repositories/{repo}/documents/{path}");
            if (OutputFormatter.JsonMode)
                OutputFormatter.Print(result!);
            else
                Console.WriteLine(result!.Content ?? "(empty)");
        }, viewRepoArg, viewPathArg);

        var createCmd = new Command("create", "Create a document");
        var createRepoArg = new Argument<string>("repo", "Repository slug");
        var createPathArg = new Argument<string>("path", "Document path");
        var fileOpt = new Option<string?>("--file", "Read content from file (- for stdin)");
        var contentOpt = new Option<string?>("--content", "Inline content");
        var messageOpt = new Option<string>("--message", () => "Initial content", "Commit message");
        createCmd.AddArgument(createRepoArg);
        createCmd.AddArgument(createPathArg);
        createCmd.AddOption(fileOpt);
        createCmd.AddOption(contentOpt);
        createCmd.AddOption(messageOpt);
        createCmd.SetHandler(async (repo, path, file, content, message) =>
        {
            var body = content;
            if (file == "-")
                body = await Console.In.ReadToEndAsync();
            else if (file is not null)
                body = await File.ReadAllTextAsync(file);

            var client = new ApiClient();
            var result = await client.PostAsync<DocResponse>($"/api/v1/repositories/{repo}/documents",
                new { path, content = body, message });
            Console.WriteLine($"Created: {result!.Path}");
        }, createRepoArg, createPathArg, fileOpt, contentOpt, messageOpt);

        var editCmd = new Command("edit", "Update a document");
        var editRepoArg = new Argument<string>("repo", "Repository slug");
        var editPathArg = new Argument<string>("path", "Document path");
        var editFileOpt = new Option<string?>("--file", "Read content from file (- for stdin)");
        var editContentOpt = new Option<string?>("--content", "Inline content");
        var editMessageOpt = new Option<string>("--message", () => "Update content", "Commit message");
        editCmd.AddArgument(editRepoArg);
        editCmd.AddArgument(editPathArg);
        editCmd.AddOption(editFileOpt);
        editCmd.AddOption(editContentOpt);
        editCmd.AddOption(editMessageOpt);
        editCmd.SetHandler(async (repo, path, file, content, message) =>
        {
            var body = content;
            if (file == "-")
                body = await Console.In.ReadToEndAsync();
            else if (file is not null)
                body = await File.ReadAllTextAsync(file);

            var client = new ApiClient();
            await client.PutAsync<DocResponse>($"/api/v1/repositories/{repo}/documents/{path}",
                new { content = body, message });
            Console.WriteLine($"Updated: {path}");
        }, editRepoArg, editPathArg, editFileOpt, editContentOpt, editMessageOpt);

        var historyCmd = new Command("history", "View revision history");
        var histRepoArg = new Argument<string>("repo", "Repository slug");
        var histPathArg = new Argument<string>("path", "Document path");
        historyCmd.AddArgument(histRepoArg);
        historyCmd.AddArgument(histPathArg);
        historyCmd.SetHandler(async (repo, path) =>
        {
            var client = new ApiClient();
            var result = await client.GetAsync<RevisionListResponse>($"/api/v1/repositories/{repo}/revisions/{path}");
            OutputFormatter.PrintTable(
                ["ID", "Message", "Author", "Date"],
                result!.Items.Select(r => new[] { r.Id[..8], r.Message, r.CreatedBy, r.CreatedAt }));
        }, histRepoArg, histPathArg);

        cmd.AddCommand(listCmd);
        cmd.AddCommand(viewCmd);
        cmd.AddCommand(createCmd);
        cmd.AddCommand(editCmd);
        cmd.AddCommand(historyCmd);

        return cmd;
    }

    private record DocResponse(string Id, string Path, string? Content, string CreatedBy, string CreatedAt, string? UpdatedAt);
    private record DocSummary(string Id, string Path, string CreatedBy, string CreatedAt);
    private record DocListResponse(List<DocSummary> Items, int Total);
    private record RevisionSummary(string Id, string Message, string CreatedBy, string CreatedAt);
    private record RevisionListResponse(List<RevisionSummary> Items, int Total);
}
