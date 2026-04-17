using System.CommandLine;

namespace Scribegate.Cli.Commands;

public static class ReviewCommands
{
    public static Command Create()
    {
        var cmd = new Command("review", "Review management");

        var listCmd = new Command("list", "List reviews on a proposal");
        var listRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var listProposalArg = new Argument<string>("proposal", "Proposal ID");
        listCmd.AddArgument(listRepoArg);
        listCmd.AddArgument(listProposalArg);
        listCmd.SetHandler(async (repo, proposalId) =>
        {
            var rr = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            var result = await client.GetAsync<ReviewListResponse>(
                $"/api/v1/repositories/{rr.Owner}/{rr.Slug}/proposals/{proposalId}/reviews");
            OutputFormatter.PrintTable(
                ["ID", "Verdict", "Author", "Date"],
                result!.Items.Select(r => new[] { r.Id[..8], r.Verdict, r.CreatedBy, r.CreatedAt }));
        }, listRepoArg, listProposalArg);

        var createCmd = new Command("create", "Submit a review");
        var createRepoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        var createProposalArg = new Argument<string>("proposal", "Proposal ID");
        var verdictOpt = new Option<string>("--verdict", "Verdict (Approved, ChangesRequested, Comment)") { IsRequired = true };
        var bodyOpt = new Option<string?>("--body", "Review comment");
        createCmd.AddArgument(createRepoArg);
        createCmd.AddArgument(createProposalArg);
        createCmd.AddOption(verdictOpt);
        createCmd.AddOption(bodyOpt);
        createCmd.SetHandler(async (repo, proposalId, verdict, body) =>
        {
            var rr = RepoRefParser.Parse(repo);
            var client = new ApiClient();
            await client.PostAsync<ReviewResponse>(
                $"/api/v1/repositories/{rr.Owner}/{rr.Slug}/proposals/{proposalId}/reviews",
                new { verdict, body });
            Console.WriteLine($"Review submitted: {verdict}");
        }, createRepoArg, createProposalArg, verdictOpt, bodyOpt);

        cmd.AddCommand(listCmd);
        cmd.AddCommand(createCmd);

        return cmd;
    }

    private record ReviewResponse(string Id, string Verdict, string? Body, string CreatedBy, string CreatedAt);
    private record ReviewListResponse(List<ReviewResponse> Items, int Total);
}
