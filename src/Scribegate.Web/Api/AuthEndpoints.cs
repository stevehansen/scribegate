using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Data;
using Scribegate.Web.Models;

namespace Scribegate.Web.Api;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous();
        group.MapGet("/me", GetMe).RequireAuthorization();
        group.MapPost("/tokens", CreateApiToken).RequireAuthorization();
        group.MapGet("/tokens", ListApiTokens).RequireAuthorization();
        group.MapDelete("/tokens/{id:guid}", DeleteApiToken).RequireAuthorization();

        return group;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        ScribegateDbContext db,
        JwtService jwt,
        CancellationToken ct)
    {
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

        if (errors.Count > 0)
            return ApiResults.ValidationError(errors);

        var username = request.Username!.Trim().ToLowerInvariant();
        var email = request.Email!.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return ApiResults.Conflict(
                "USERNAME_TAKEN",
                $"The username '{username}' is already taken.",
                "Try a different username, or login if this is your account.",
                "username");

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return ApiResults.Conflict(
                "EMAIL_TAKEN",
                "An account with this email already exists.",
                "Try logging in instead, or use a different email address.",
                "email");

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

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
        ScribegateDbContext db,
        JwtService jwt,
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

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
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

        var token = jwt.GenerateToken(user);

        return Results.Ok(new AuthResponse
        {
            Token = token,
            ExpiresAt = jwt.GetExpiration(),
            User = MapToUserInfo(user),
        });
    }

    private static async Task<IResult> GetMe(
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Unauthorized();

        var user = await db.Users.FindAsync([userId.Value], ct);
        if (user is null)
            return Unauthorized();

        return Results.Ok(MapToUserInfo(user));
    }

    private static async Task<IResult> CreateApiToken(
        CreateApiTokenRequest request,
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return ApiResults.ValidationError("name", ApiErrorCodes.Required,
                "Token name is required.",
                "Provide a descriptive name for this token (e.g., 'CI pipeline', 'My AI assistant').");

        if (request.Name.Trim().Length > 200)
            return ApiResults.ValidationError("name", ApiErrorCodes.TooLong,
                "Token name must be 200 characters or less.");

        var rawToken = ApiTokenAuthHandler.GenerateToken();
        var tokenHash = ApiTokenAuthHandler.HashToken(rawToken);

        var apiToken = new ApiToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId.Value,
            Name = request.Name.Trim(),
            TokenHash = tokenHash,
            Scopes = request.Scopes?.Trim(),
            ExpiresAt = request.ExpiresInDays.HasValue
                ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
                : null,
        };

        db.ApiTokens.Add(apiToken);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/auth/tokens", new ApiTokenCreatedResponse
        {
            Id = apiToken.Id,
            Name = apiToken.Name,
            Token = rawToken,
            Scopes = apiToken.Scopes,
            CreatedAt = apiToken.CreatedAt,
            ExpiresAt = apiToken.ExpiresAt,
        });
    }

    private static async Task<IResult> ListApiTokens(
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Unauthorized();

        var tokens = await db.ApiTokens
            .Where(t => t.UserId == userId.Value)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ApiTokenResponse
            {
                Id = t.Id,
                Name = t.Name,
                Scopes = t.Scopes,
                CreatedAt = t.CreatedAt,
                ExpiresAt = t.ExpiresAt,
                LastUsedAt = t.LastUsedAt,
            })
            .ToListAsync(ct);

        return Results.Ok(tokens);
    }

    private static async Task<IResult> DeleteApiToken(
        Guid id,
        ClaimsPrincipal principal,
        ScribegateDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Unauthorized();

        var token = await db.ApiTokens.FirstOrDefaultAsync(
            t => t.Id == id && t.UserId == userId.Value, ct);

        if (token is null)
            return ApiResults.NotFound("API token", id.ToString());

        db.ApiTokens.Remove(token);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
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
        CreatedAt = user.CreatedAt,
    };
}
