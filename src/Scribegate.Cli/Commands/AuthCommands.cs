using System.CommandLine;

namespace Scribegate.Cli.Commands;

public static class AuthCommands
{
    public static Command Create()
    {
        var cmd = new Command("auth", "Authentication and API tokens");

        var loginCmd = new Command("login", "Login with email and password");
        var emailArg = new Argument<string>("email", "Email address");
        var passwordArg = new Argument<string>("password", "Password");
        var hostOption = new Option<string?>("--host", "Server URL");
        loginCmd.AddArgument(emailArg);
        loginCmd.AddArgument(passwordArg);
        loginCmd.AddOption(hostOption);
        loginCmd.SetHandler(async (email, password, host) =>
        {
            var config = CliConfig.Load();
            if (host is not null) config.Host = host;

            var client = new ApiClient();
            var result = await client.PostAsync<LoginResponse>("/api/v1/auth/login",
                new { email, password });

            config.Token = result!.Token;
            config.Save();

            Console.WriteLine($"Logged in as {result.User.Username}");
            Console.WriteLine($"Token saved to config. Expires: {result.ExpiresAt}");
        }, emailArg, passwordArg, hostOption);

        var tokenCmd = new Command("token", "Set an API token directly");
        var tokenArg = new Argument<string>("token", "API token (sg_...)");
        tokenCmd.AddArgument(tokenArg);
        tokenCmd.SetHandler(token =>
        {
            var config = CliConfig.Load();
            config.Token = token;
            config.Save();
            Console.WriteLine("Token saved.");
        }, tokenArg);

        var statusCmd = new Command("status", "Show current auth status");
        statusCmd.SetHandler(async () =>
        {
            try
            {
                var client = new ApiClient();
                var user = await client.GetAsync<UserInfoDto>("/api/v1/auth/me");
                OutputFormatter.Print(new
                {
                    user!.Username,
                    user.Email,
                    user.IsAdmin,
                    user.CreatedAt,
                });
            }
            catch
            {
                Console.WriteLine("Not authenticated or server unreachable.");
            }
        });

        cmd.AddCommand(loginCmd);
        cmd.AddCommand(tokenCmd);
        cmd.AddCommand(statusCmd);

        return cmd;
    }

    private record LoginResponse(string Token, string ExpiresAt, UserInfoDto User);
    private record UserInfoDto(string Id, string Username, string Email, bool IsAdmin, string CreatedAt);
}
