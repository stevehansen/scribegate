using Scribegate.Core.Entities;
using Scribegate.Core.Events;
using Scribegate.Core.Stores;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting("auth");
        group.MapGet("/me", GetMe).RequireAuthorization();
        group.MapGet("/me/quota", GetMyQuota).RequireAuthorization();
        group.MapPut("/preferences", UpdatePreferences).RequireAuthorization();
        group.MapPost("/tokens", CreateApiToken).RequireAuthorization();
        group.MapGet("/tokens", ListApiTokens).RequireAuthorization();
        group.MapDelete("/tokens/{id:guid}", DeleteApiToken).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        IUserStore users,
        JwtService jwt,
        ISystemSettingStore settings,
        IDomainEventBus events,
        TierService tierService,
        CancellationToken ct)
    {
        // Check if registration is enabled
        var regEnabled = await settings.GetAsync(SystemSettingKeys.RegistrationEnabled, ct);
        if (regEnabled == "false")
        {
            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "REGISTRATION_DISABLED",
                    Message = "Registration is currently disabled.",
                    Details = "Contact an administrator to request access, or ask them to enable registration in the admin settings.",
                }
            }, statusCode: 403);
        }

        var errors = new List<ApiFieldError>();

        if (string.IsNullOrWhiteSpace(request.Username))
            errors.Add(new ApiFieldError
            {
                Field = "username",
                Code = ApiErrorCodes.Required,
                Message = "Username is required.",
                Details = "Choose a username (3-100 characters, alphanumeric and hyphens).",
            });
        else if (request.Username.Trim().Length < 3)
            errors.Add(new ApiFieldError
            {
                Field = "username",
                Code = ApiErrorCodes.InvalidFormat,
                Message = "Username must be at least 3 characters.",
            });
        else if (request.Username.Trim().Length > 100)
            errors.Add(new ApiFieldError
            {
                Field = "username",
                Code = ApiErrorCodes.TooLong,
                Message = "Username must be 100 characters or less.",
            });

        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add(new ApiFieldError
            {
                Field = "email",
                Code = ApiErrorCodes.Required,
                Message = "Email is required.",
            });
        else if (!request.Email.Contains('@') || !request.Email.Contains('.'))
            errors.Add(new ApiFieldError
            {
                Field = "email",
                Code = ApiErrorCodes.InvalidFormat,
                Message = "Email must be a valid email address.",
            });

        if (string.IsNullOrEmpty(request.Password))
            errors.Add(new ApiFieldError
            {
                Field = "password",
                Code = ApiErrorCodes.Required,
                Message = "Password is required.",
                Details = "Choose a password (10-128 characters).",
            });
        else if (request.Password.Length < 10)
            errors.Add(new ApiFieldError
            {
                Field = "password",
                Code = ApiErrorCodes.InvalidFormat,
                Message = "Password must be at least 10 characters.",
                Details = "Use a passphrase or longer password. No complexity rules — just make it long enough to be secure.",
            });
        else if (request.Password.Length > 128)
            errors.Add(new ApiFieldError
            {
                Field = "password",
                Code = ApiErrorCodes.TooLong,
                Message = "Password must be 128 characters or less.",
            });

        // ToS acceptance check
        var requireTos = await settings.GetAsync(SystemSettingKeys.RequireTos, ct);
        if (requireTos != "false" && !request.AcceptTos)
            errors.Add(new ApiFieldError
            {
                Field = "acceptTos",
                Code = ApiErrorCodes.Required,
                Message = "You must accept the Terms of Service to register.",
                Details = "Set acceptTos to true to indicate you have read and accept the Terms of Service.",
            });

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var username = request.Username!.Trim().ToLowerInvariant();
        var email = request.Email!.Trim().ToLowerInvariant();

        if (await users.UsernameExistsAsync(username, ct))
            return ApiResults.Conflict(
                "USERNAME_TAKEN",
                $"The username '{username}' is already taken.",
                "Try a different username, or login if this is your account.",
                "username");

        if (await users.EmailExistsAsync(email, ct))
            return ApiResults.Conflict(
                "EMAIL_TAKEN",
                "An account with this email already exists.",
                "Try logging in instead, or use a different email address.",
                "email");

        // First user becomes admin
        var isFirstUser = !await users.AnyExistAsync(ct);

        var defaultTier = await tierService.GetDefaultTierAsync(ct);

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsAdmin = isFirstUser,
            Tier = defaultTier,
            // Password-account email verification is not implemented yet.
            // Keep accounts usable instead of minting permanently unverified users.
            EmailVerified = true,
            TosAcceptedAt = request.AcceptTos ? DateTime.UtcNow : null,
        };

        await users.CreateAsync(user, ct);

        await events.PublishAsync(new UserRegisteredEvent(
            UserId: user.Id,
            Username: user.Username,
            IsFirstUser: isFirstUser,
            IsAdmin: user.IsAdmin,
            Provider: null,
            OccurredAt: DateTime.UtcNow), ct);

        var token = jwt.GenerateToken(user);

        return Results.Created($"/api/v1/auth/me", new AuthResponse
        {
            Token = token,
            ExpiresAt = jwt.GetExpiration(),
            User = MapToUserInfo(user),
        });
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IUserStore users,
        JwtService jwt,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();

        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add(new ApiFieldError { Field = "email", Code = ApiErrorCodes.Required, Message = "Email is required." });

        if (string.IsNullOrEmpty(request.Password))
            errors.Add(new ApiFieldError { Field = "password", Code = ApiErrorCodes.Required, Message = "Password is required." });

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var email = request.Email!.Trim().ToLowerInvariant();

        var user = await users.FindByEmailAsync(email, ct);
        if (user is null || user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await events.PublishAsync(new UserLoginFailedEvent(
                Email: email,
                OccurredAt: DateTime.UtcNow), ct);

            return Results.Json(new
            {
                error = new ApiError
                {
                    Code = "INVALID_CREDENTIALS",
                    Message = "Invalid email or password.",
                    Details = "Check your email and password and try again. If you don't have an account, register first.",
                }
            }, statusCode: 401);
        }

        await events.PublishAsync(new UserLoggedInEvent(
            UserId: user.Id,
            Username: user.Username,
            Provider: null,
            OccurredAt: DateTime.UtcNow), ct);

        var token = jwt.GenerateToken(user);

        return Results.Ok(new AuthResponse
        {
            Token = token,
            ExpiresAt = jwt.GetExpiration(),
            User = MapToUserInfo(user),
        });
    }

    private static async Task<IResult> GetMe(
        UserContext userContext,
        CancellationToken ct)
    {
        var user = await userContext.GetCurrentUserAsync(ct);
        if (user is null)
            return Unauthorized();

        return Results.Ok(MapToUserInfo(user));
    }

    private static async Task<IResult> GetMyQuota(
        UserContext userContext,
        IApiTokenStore apiTokens,
        TierService tierService,
        IMembershipStore membershipStore,
        CancellationToken ct)
    {
        var user = await userContext.GetCurrentUserAsync(ct);
        if (user is null) return Unauthorized();

        var enforced = await tierService.IsEnforcedAsync(ct);
        var limits = await tierService.GetLimitsForUserAsync(user, ct);

        var repoCount = await membershipStore.CountRepositoriesOwnedByUserAsync(user.Id, ct);
        var tokenCount = await apiTokens.CountActiveByUserAsync(user.Id, ct);

        return Results.Ok(new
        {
            tier = user.Tier,
            enforced,
            limits = new
            {
                maxRepositories = limits.MaxRepositories,
                maxDocumentsPerRepo = limits.MaxDocumentsPerRepo,
                maxStorageMb = limits.MaxStorageMb,
                maxApiTokens = limits.MaxApiTokens,
                maxMembersPerRepo = limits.MaxMembersPerRepo,
            },
            usage = new
            {
                repositories = repoCount,
                apiTokens = tokenCount,
            },
        });
    }

    private static readonly HashSet<string> ValidThemes = ["light", "dark", "system"];

    private static async Task<IResult> UpdatePreferences(
        UpdatePreferencesRequest request,
        UserContext userContext,
        IUserStore users,
        CancellationToken ct)
    {
        var user = await userContext.GetCurrentUserAsync(ct);
        if (user is null)
            return Unauthorized();

        if (request.ThemePreference is not null)
        {
            if (!ValidThemes.Contains(request.ThemePreference))
                return ApiResults.ValidationError("themePreference", ApiErrorCodes.InvalidFormat,
                    "Theme must be 'light', 'dark', or 'system'.");
            user.ThemePreference = request.ThemePreference;
        }

        await users.UpdateAsync(user, ct);
        userContext.InvalidateCurrentUser();
        return Results.Ok(MapToUserInfo(user));
    }

    private static async Task<IResult> CreateApiToken(
        CreateApiTokenRequest request,
        UserContext userContext,
        IApiTokenStore apiTokens,
        IDomainEventBus events,
        TierService tierService,
        CancellationToken ct)
    {
        var user = await userContext.GetCurrentUserAsync(ct);
        if (user is null)
            return Unauthorized();
        var userId = user.Id;

        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiResults.ValidationError("name", ApiErrorCodes.Required,
                "Token name is required.",
                "Provide a descriptive name for this token (e.g., 'CI pipeline', 'My AI assistant').");

        if (request.Name.Trim().Length > 200)
            return ApiResults.ValidationError("name", ApiErrorCodes.TooLong,
                "Token name must be 200 characters or less.");

        if (!string.IsNullOrWhiteSpace(request.Scopes))
            return ApiResults.ValidationError("scopes", ApiErrorCodes.InvalidFormat,
                "API token scopes are not supported yet.",
                "Leave scopes empty for now. Scoped API tokens will be rejected until enforcement exists.");

        // Quota check: max API tokens
        {
            var limits = await tierService.GetLimitsForUserAsync(user, ct);
            if (!limits.IsUnlimited(limits.MaxApiTokens))
            {
                var tokenCount = await apiTokens.CountActiveByUserAsync(userId, ct);
                if (tokenCount >= limits.MaxApiTokens)
                    return Results.Json(new
                    {
                        error = new ApiError
                        {
                            Code = ApiErrorCodes.QuotaExceeded,
                            Message = $"You have reached the maximum of {limits.MaxApiTokens} API tokens for your plan.",
                            Details = $"Your {user.Tier} plan allows up to {limits.MaxApiTokens} API tokens. Revoke an existing token or upgrade your plan.",
                        }
                    }, statusCode: 403);
            }
        }

        var rawToken = ApiTokenAuthHandler.GenerateToken();
        var tokenHash = ApiTokenAuthHandler.HashToken(rawToken);

        var apiToken = new ApiToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = request.Name.Trim(),
            TokenHash = tokenHash,
            Scopes = request.Scopes?.Trim(),
            ExpiresAt = request.ExpiresInDays.HasValue
                ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
                : null,
        };

        await apiTokens.CreateAsync(apiToken, ct);

        await events.PublishAsync(new ApiTokenCreatedEvent(
            TokenId: apiToken.Id,
            UserId: userId,
            TokenName: apiToken.Name,
            ActorUsername: user.Username,
            OccurredAt: DateTime.UtcNow), ct);

        return Results.Created($"/api/v1/auth/tokens", new ApiTokenCreatedResponse
        {
            Id = apiToken.Id,
            Name = apiToken.Name,
            Token = rawToken,
            Scopes = null,
            CreatedAt = apiToken.CreatedAt,
            ExpiresAt = apiToken.ExpiresAt,
        });
    }

    private static async Task<IResult> ListApiTokens(
        UserContext userContext,
        IApiTokenStore apiTokens,
        CancellationToken ct)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var tokens = await apiTokens.ListByUserAsync(userId.Value, ct);
        return Results.Ok(tokens.Select(t => new ApiTokenResponse
        {
            Id = t.Id,
            Name = t.Name,
            Scopes = null,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            LastUsedAt = t.LastUsedAt,
        }).ToList());
    }

    private static async Task<IResult> DeleteApiToken(
        Guid id,
        UserContext userContext,
        IApiTokenStore apiTokens,
        IDomainEventBus events,
        CancellationToken ct)
    {
        var userId = userContext.TryGetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var tokens = await apiTokens.ListByUserAsync(userId.Value, ct);
        var token = tokens.FirstOrDefault(t => t.Id == id);

        if (token is null)
            return ApiResults.NotFound("API token", id.ToString());

        await apiTokens.RevokeAsync(id, ct);

        await events.PublishAsync(new ApiTokenRevokedEvent(
            TokenId: id,
            ActorId: userId.Value,
            ActorUsername: userContext.GetUsername(),
            TokenName: token.Name,
            OccurredAt: DateTime.UtcNow), ct);

        return Results.NoContent();
    }

    private static IResult Unauthorized() =>
        Results.Json(new
        {
            error = new ApiError
            {
                Code = "UNAUTHORIZED",
                Message = "Authentication required.",
                Details = "Include a valid JWT token in the Authorization header: Bearer <token>. Get a token via POST /api/v1/auth/login.",
            }
        }, statusCode: 401);

    private static UserInfo MapToUserInfo(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        IsAdmin = user.IsAdmin,
        Tier = user.Tier,
        ThemePreference = user.ThemePreference,
        CreatedAt = user.CreatedAt,
    };
}
