using System.CommandLine;

namespace Scribegate.Cli.Commands;

public static class AuthCommands
{
    public static Command Create()
    {
        var cmd = new Command("auth", "Authentication and API tokens");

        var hostOption = new Option<string?>("--host", "Server URL (e.g. https://scribegate.dev). Saved with your credentials.");

        var loginCmd = new Command("login", "Login with email and password");
        var emailArg = new Argument<string>("email", "Email address");
        var passwordArg = new Argument<string>("password", "Password");
        loginCmd.AddArgument(emailArg);
        loginCmd.AddArgument(passwordArg);
        loginCmd.AddOption(hostOption);
        loginCmd.SetHandler(async (email, password, host) =>
        {
            var config = CliConfig.Load();
            var targetHost = host is not null ? CliConfig.NormalizeHost(host) : config.ResolvedHost;

            try
            {
                var client = new ApiClient(targetHost, tokenOverride: null);
                var result = await client.PostAsync<LoginResponse>("/api/v1/auth/login",
                    new { email, password });

                config.Host = targetHost;
                config.Token = result!.Token;
                config.Save();

                Console.WriteLine($"Logged in as {result.User.Username} on {targetHost}");
                Console.WriteLine($"Token saved. Expires: {result.ExpiresAt}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Login failed against {targetHost}: {ex.Message}");
                Console.Error.WriteLine("Configuration was not changed.");
                Environment.ExitCode = 1;
            }
        }, emailArg, passwordArg, hostOption);

        var tokenCmd = new Command("token", "Set an API token directly (validated against the server before saving)");
        var tokenArg = new Argument<string>("token", "API token (sg_...)");
        tokenCmd.AddArgument(tokenArg);
        tokenCmd.AddOption(hostOption);
        tokenCmd.SetHandler(async (token, host) =>
        {
            var config = CliConfig.Load();
            var targetHost = host is not null ? CliConfig.NormalizeHost(host) : config.ResolvedHost;

            try
            {
                var client = new ApiClient(targetHost, tokenOverride: token);
                var user = await client.GetAsync<UserInfoDto>("/api/v1/auth/me");

                config.Host = targetHost;
                config.Token = token;
                config.Save();

                Console.WriteLine($"Token saved for {user!.Username} on {targetHost}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Token validation failed against {targetHost}: {ex.Message}");
                Console.Error.WriteLine("Configuration was not changed.");
                Environment.ExitCode = 1;
            }
        }, tokenArg, hostOption);

        var hostCmd = new Command("host", "Show or set the server host");
        var hostUrlArg = new Argument<string?>("url", () => null, "Server URL (e.g. https://scribegate.dev). Omit to show the current host.");
        var skipValidationOption = new Option<bool>("--no-validate", "Skip the /healthz probe before saving");
        hostCmd.AddArgument(hostUrlArg);
        hostCmd.AddOption(skipValidationOption);
        hostCmd.SetHandler(async (url, skipValidation) =>
        {
            var config = CliConfig.Load();
            if (url is null)
            {
                Console.WriteLine($"Host: {config.ResolvedHost}{(config.Host is null ? " (default)" : "")}");
                return;
            }

            var targetHost = CliConfig.NormalizeHost(url);

            if (!skipValidation)
            {
                try
                {
                    var client = new ApiClient(targetHost, tokenOverride: null);
                    await client.GetAsync<object>("/healthz");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not reach {targetHost}: {ex.Message}");
                    Console.Error.WriteLine("Host was not changed. Use --no-validate to save anyway.");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            config.Host = targetHost;
            config.Save();
            Console.WriteLine($"Host set to {targetHost}.");
        }, hostUrlArg, skipValidationOption);

        var statusCmd = new Command("status", "Show current auth status");
        statusCmd.SetHandler(async () =>
        {
            var config = CliConfig.Load();
            var host = config.ResolvedHost;
            var hasToken = !string.IsNullOrEmpty(config.Token)
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCRIBEGATE_TOKEN"));

            if (!hasToken)
            {
                OutputFormatter.Print(new
                {
                    Host = host,
                    Authenticated = false,
                    Message = "No token configured. Run `sg auth login` or `sg auth token`.",
                });
                return;
            }

            try
            {
                var client = new ApiClient();
                var user = await client.GetAsync<UserInfoDto>("/api/v1/auth/me");
                OutputFormatter.Print(new
                {
                    Host = host,
                    Authenticated = true,
                    user!.Username,
                    user.Email,
                    user.IsAdmin,
                    user.CreatedAt,
                });
            }
            catch (Exception ex)
            {
                OutputFormatter.Print(new
                {
                    Host = host,
                    Authenticated = false,
                    Error = ex.Message,
                });
                Environment.ExitCode = 1;
            }
        });

        cmd.AddCommand(loginCmd);
        cmd.AddCommand(tokenCmd);
        cmd.AddCommand(hostCmd);
        cmd.AddCommand(statusCmd);

        return cmd;
    }

    private record LoginResponse(string Token, string ExpiresAt, UserInfoDto User);
    private record UserInfoDto(string Id, string Username, string Email, bool IsAdmin, string CreatedAt);
}
