namespace Scribegate.Core.Events;

/// <summary>
/// A user authenticated successfully. <see cref="Provider"/> is null for
/// password logins (audit details = null) and the OIDC provider name for
/// SSO logins (audit details = { provider, viaOidc }).
/// </summary>
public sealed record UserLoggedInEvent(
    Guid UserId,
    string Username,
    string? Provider,
    DateTime OccurredAt) : IImmediateEvent;
