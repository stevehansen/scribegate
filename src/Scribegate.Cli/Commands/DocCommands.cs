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

        var shareCmd = new Command("share", "Create a share link for a document");
        var shareRepoArg = new Argument<string>("repo", "Repository slug");
        var sharePathArg = new Argument<string>("path", "Document path");
        var shareExpiresOpt = new Option<int?>("--expires", "Days until the link expires (default 7)");
        var sharePermanentOpt = new Option<bool>("--permanent", "Create a link that never expires");
        var shareDescOpt = new Option<string?>("--description", "Optional description");
        var shareRevisionOpt = new Option<string?>("--revision", "Pin the link to a specific revision ID (default: always show latest)");
        shareCmd.AddArgument(shareRepoArg);
        shareCmd.AddArgument(sharePathArg);
        shareCmd.AddOption(shareExpiresOpt);
        shareCmd.AddOption(sharePermanentOpt);
        shareCmd.AddOption(shareDescOpt);
        shareCmd.AddOption(shareRevisionOpt);
        shareCmd.SetHandler(async (repo, path, expires, permanent, description, revision) =>
        {
            Guid? revisionId = null;
            if (!string.IsNullOrWhiteSpace(revision))
            {
                if (!Guid.TryParse(revision, out var rid))
                    throw new CliException("--revision must be a GUID.");
                revisionId = rid;
            }

            var client = new ApiClient();
            var result = await client.PostAsync<ShareLinkCreatedResponse>(
                $"/api/v1/repositories/{repo}/shares",
                new { path, description, expiresInDays = expires, permanent, revisionId });

            var host = CliConfig.Load().Host ?? "http://localhost:5199";
            var fullUrl = host.TrimEnd('/') + result!.Url;

            if (OutputFormatter.JsonMode)
                OutputFormatter.Print(new { result.Id, result.Token, url = fullUrl, result.Description, result.CreatedAt, result.ExpiresAt });
            else
            {
                Console.WriteLine($"Share link created: {fullUrl}");
                Console.WriteLine($"Expires: {result.ExpiresAt ?? "never"}");
                Console.WriteLine($"Token: {result.Token}  (store this — it won't be shown again)");
            }
        }, shareRepoArg, sharePathArg, shareExpiresOpt, sharePermanentOpt, shareDescOpt, shareRevisionOpt);

        var sharesCmd = new Command("shares", "List share links for a repository or document");
        var sharesRepoArg = new Argument<string>("repo", "Repository slug");
        var sharesPathOpt = new Option<string?>("--path", "Filter to share links for a single document");
        sharesCmd.AddArgument(sharesRepoArg);
        sharesCmd.AddOption(sharesPathOpt);
        sharesCmd.SetHandler(async (repo, path) =>
        {
            var url = $"/api/v1/repositories/{repo}/shares";
            if (!string.IsNullOrWhiteSpace(path))
                url += $"?path={Uri.EscapeDataString(path)}";

            var client = new ApiClient();
            var result = await client.GetAsync<ShareLinkListResponse>(url);

            OutputFormatter.PrintTable(
                ["ID", "Path", "Prefix", "Active", "Expires", "Accesses", "Created By"],
                result!.Items.Select(s => new[]
                {
                    s.Id[..8],
                    s.DocumentPath,
                    s.TokenPrefix,
                    s.IsActive ? "yes" : "no",
                    s.ExpiresAt ?? "never",
                    s.AccessCount.ToString(),
                    s.CreatedBy,
                }));
        }, sharesRepoArg, sharesPathOpt);

        var unshareCmd = new Command("unshare", "Revoke a share link");
        var unshareRepoArg = new Argument<string>("repo", "Repository slug");
        var unshareIdArg = new Argument<string>("id", "Share link ID (or a unique prefix)");
        unshareCmd.AddArgument(unshareRepoArg);
        unshareCmd.AddArgument(unshareIdArg);
        unshareCmd.SetHandler(async (repo, id) =>
        {
            var client = new ApiClient();
            if (!Guid.TryParse(id, out _))
                throw new CliException("ID must be a full GUID. Use 'sg doc shares <repo>' to see full IDs.");
            await client.DeleteAsync($"/api/v1/repositories/{repo}/shares/{id}");
            Console.WriteLine("Share link revoked.");
        }, unshareRepoArg, unshareIdArg);

        cmd.AddCommand(listCmd);
        cmd.AddCommand(viewCmd);
        cmd.AddCommand(createCmd);
        cmd.AddCommand(editCmd);
        cmd.AddCommand(historyCmd);
        cmd.AddCommand(shareCmd);
        cmd.AddCommand(sharesCmd);
        cmd.AddCommand(unshareCmd);

        return cmd;
    }

    private record DocResponse(string Id, string Path, string? Content, string CreatedBy, string CreatedAt, string? UpdatedAt);
    private record DocSummary(string Id, string Path, string CreatedBy, string CreatedAt);
    private record DocListResponse(List<DocSummary> Items, int Total);
    private record RevisionSummary(string Id, string Message, string CreatedBy, string CreatedAt);
    private record RevisionListResponse(List<RevisionSummary> Items, int Total);

    private record ShareLinkCreatedResponse(string Id, string Token, string Url, string? Description, string CreatedAt, string? ExpiresAt);
    private record ShareLinkSummary(string Id, string TokenPrefix, string? Description, string DocumentPath, string? RevisionId, string CreatedBy, string CreatedAt, string? ExpiresAt, string? RevokedAt, string? LastAccessedAt, int AccessCount, bool IsActive);
    private record ShareLinkListResponse(List<ShareLinkSummary> Items, int Total);
}
