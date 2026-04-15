using System.CommandLine;
using Scribegate.Cli;
using Scribegate.Cli.Commands;

var rootCommand = new RootCommand("Scribegate CLI — markdown collaboration platform")
{
    AuthCommands.Create(),
    RepoCommands.Create(),
    DocCommands.Create(),
    ProposalCommands.Create(),
    ReviewCommands.Create(),
};

var jsonOption = new Option<bool>("--json", "Output in JSON format");
rootCommand.AddGlobalOption(jsonOption);

rootCommand.SetHandler(_ => { }, jsonOption);

// Set JSON mode before execution
rootCommand.AddValidator(result =>
{
    if (result.GetValueForOption(jsonOption))
        OutputFormatter.JsonMode = true;
});

return await rootCommand.InvokeAsync(args);
