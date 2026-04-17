using System.CommandLine;

namespace Scribegate.Cli.Commands;

public static class ProposalCommands
{
    public static Command Create()
    {
        var cmd = new Command("proposal", "Proposal management");

        var listCmd = new Command("list", "List proposals");
        var listRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var statusOpt = new Option<string?>("--status", "Filter by status (Open, Approved, Rejected, Withdrawn)");
        listCmd.AddArgument(listRepoArg);
        listCmd.AddOption(statusOpt);
        listCmd.SetHandler(async (repo, status) =>
        {
            var r = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            var url = $"/api/v1/repositories/{r.Owner}/{r.Slug}/proposals";
            if (status is not null) url += $"?status={status}";
            var result = await client.GetAsync<ProposalListResponse>(url);
            OutputFormatter.PrintTable(
                ["ID", "Title", "Status", "Author", "Date"],
                result!.Items.Select(p => new[] { p.Id[..8], p.Title, p.Status, p.CreatedBy, p.CreatedAt }));
        }, listRepoArg, statusOpt);

        var createCmd = new Command("create", "Create a proposal");
        var createRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var titleOpt = new Option<string>("--title", "Proposal title") { IsRequired = true };
        var docPathOpt = new Option<string?>("--document", "Document path");
        var fileOpt = new Option<string?>("--file", "Read content from file (- for stdin)");
        var contentOpt = new Option<string?>("--content", "Inline content");
        var descOpt = new Option<string?>("--description", "Proposal description");
        createCmd.AddArgument(createRepoArg);
        createCmd.AddOption(titleOpt);
        createCmd.AddOption(docPathOpt);
        createCmd.AddOption(fileOpt);
        createCmd.AddOption(contentOpt);
        createCmd.AddOption(descOpt);
        createCmd.SetHandler(async (repo, title, docPath, file, content, desc) =>
        {
            var r = RepoRefParser.Parse(repo);
            var body = content;
            if (file == "-")
                body = await Console.In.ReadToEndAsync();
            else if (file is not null)
                body = await File.ReadAllTextAsync(file);

            var client = new ApiClient();
            var result = await client.PostAsync<ProposalSummary>($"/api/v1/repositories/{r.Owner}/{r.Slug}/proposals",
                new { title, documentPath = docPath, content = body, description = desc });
            Console.WriteLine($"Created proposal: {result!.Id[..8]} - {result.Title} ({result.Status})");
        }, createRepoArg, titleOpt, docPathOpt, fileOpt, contentOpt, descOpt);

        var viewCmd = new Command("view", "View proposal details");
        var viewRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var viewIdArg = new Argument<string>("id", "Proposal ID");
        viewCmd.AddArgument(viewRepoArg);
        viewCmd.AddArgument(viewIdArg);
        viewCmd.SetHandler(async (repo, id) =>
        {
            var r = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            var result = await client.GetAsync<ProposalDetail>($"/api/v1/repositories/{r.Owner}/{r.Slug}/proposals/{id}");
            OutputFormatter.Print(result!);
        }, viewRepoArg, viewIdArg);

        var approveCmd = new Command("approve", "Approve a proposal");
        var apprRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var apprIdArg = new Argument<string>("id", "Proposal ID");
        approveCmd.AddArgument(apprRepoArg);
        approveCmd.AddArgument(apprIdArg);
        approveCmd.SetHandler(async (repo, id) =>
        {
            var r = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            await client.PostAsync($"/api/v1/repositories/{r.Owner}/{r.Slug}/proposals/{id}/approve");
            Console.WriteLine("Proposal approved.");
        }, apprRepoArg, apprIdArg);

        var rejectCmd = new Command("reject", "Reject a proposal");
        var rejRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var rejIdArg = new Argument<string>("id", "Proposal ID");
        rejectCmd.AddArgument(rejRepoArg);
        rejectCmd.AddArgument(rejIdArg);
        rejectCmd.SetHandler(async (repo, id) =>
        {
            var r = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            await client.PostAsync($"/api/v1/repositories/{r.Owner}/{r.Slug}/proposals/{id}/reject");
            Console.WriteLine("Proposal rejected.");
        }, rejRepoArg, rejIdArg);

        var withdrawCmd = new Command("withdraw", "Withdraw your proposal");
        var wdRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var wdIdArg = new Argument<string>("id", "Proposal ID");
        withdrawCmd.AddArgument(wdRepoArg);
        withdrawCmd.AddArgument(wdIdArg);
        withdrawCmd.SetHandler(async (repo, id) =>
        {
            var r = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            await client.PostAsync($"/api/v1/repositories/{r.Owner}/{r.Slug}/proposals/{id}/withdraw");
            Console.WriteLine("Proposal withdrawn.");
        }, wdRepoArg, wdIdArg);

        cmd.AddCommand(listCmd);
        cmd.AddCommand(createCmd);
        cmd.AddCommand(viewCmd);
        cmd.AddCommand(approveCmd);
        cmd.AddCommand(rejectCmd);
        cmd.AddCommand(withdrawCmd);

        return cmd;
    }

    private record ProposalSummary(string Id, string Title, string Status, string CreatedBy, string CreatedAt);
    private record ProposalListResponse(List<ProposalSummary> Items, int Total);
    private record ProposalDetail(string Id, string Title, string? Description, string Status, string ProposedContent, string CreatedBy, string CreatedAt);
}
