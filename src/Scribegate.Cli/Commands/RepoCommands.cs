using System.CommandLine;

namespace Scribegate.Cli.Commands;

public static class RepoCommands
{
    public static Command Create()
    {
        var cmd = new Command("repo", "Repository management");

        var listCmd = new Command("list", "List repositories");
        listCmd.SetHandler(async () =>
        {
            var client = new ApiClient();
            var result = await client.GetAsync<RepoListResponse>("/api/v1/repositories");
            OutputFormatter.PrintTable(
                ["Name", "Slug", "Visibility", "Documents"],
                result!.Items.Select(r => new[] { r.Name, r.Slug, r.Visibility, r.DocumentCount.ToString() }));
        });

        var createCmd = new Command("create", "Create a repository");
        var nameArg = new Argument<string>("name", "Repository name");
        var descOpt = new Option<string?>("--description", "Description");
        var visOpt = new Option<string>("--visibility", () => "Private", "Visibility (Public/Private)");
        createCmd.AddArgument(nameArg);
        createCmd.AddOption(descOpt);
        createCmd.AddOption(visOpt);
        createCmd.SetHandler(async (name, desc, vis) =>
        {
            var client = new ApiClient();
            var result = await client.PostAsync<RepoResponse>("/api/v1/repositories",
                new { name, description = desc, visibility = vis });
            OutputFormatter.Print(new { result!.Name, result.Slug, result.Visibility });
            Console.WriteLine($"Created repository: {result.Slug}");
        }, nameArg, descOpt, visOpt);

        var viewCmd = new Command("view", "View repository details");
        var repoArg = new Argument<string>("repo", "Repository reference (owner/slug)");
        viewCmd.AddArgument(repoArg);
        viewCmd.SetHandler(async (repoRef) =>
        {
            var r = RepoRefParser.Parse(repoRef);
            var client = new ApiClient();
            var result = await client.GetAsync<RepoResponse>($"/api/v1/repositories/{r.Owner}/{r.Slug}");
            OutputFormatter.Print(result!);
        }, repoArg);

        cmd.AddCommand(listCmd);
        cmd.AddCommand(createCmd);
        cmd.AddCommand(viewCmd);

        return cmd;
    }

    private record RepoResponse(string Id, string Name, string Slug, string? Description, string Visibility, int DocumentCount, string CreatedAt);
    private record RepoListResponse(List<RepoResponse> Items, int Total);
}
