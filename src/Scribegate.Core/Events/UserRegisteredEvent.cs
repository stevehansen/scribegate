namespace Scribegate.Core.Events;

/// <summary>
/// A new user account was created. <see cref="Provider"/> is null for
/// password-based registrations and the OIDC provider name (e.g., "oidc")
/// for SSO sign-ups; the audit handler renders the matching JSON details
/// shape so the wire contract stays identical to the pre-bus code.
/// </summary>
public sealed record UserRegisteredEvent(
    Guid UserId,
    string Username,
    bool IsFirstUser,
    bool IsAdmin,
    string? Provider,
    DateTime OccurredAt) : IImmediateEvent;
