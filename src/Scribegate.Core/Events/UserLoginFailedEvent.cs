namespace Scribegate.Core.Events;

/// <summary>
/// A login attempt failed at the email/password check. Anonymous —
/// no user id, no username — only the attempted email is logged.
/// </summary>
public sealed record UserLoginFailedEvent(
    string Email,
    DateTime OccurredAt) : IImmediateEvent;
