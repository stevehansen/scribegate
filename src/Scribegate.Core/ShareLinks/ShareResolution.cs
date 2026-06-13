namespace Scribegate.Core.ShareLinks;

/// <summary>
/// Lifecycle state of a share token after the resolver has validated it.
/// The Web layer maps each state to an HTTP contract in exactly one place
/// (<c>ShareResolutionExtensions.ToError()</c>), so the 404-vs-410 decision
/// can no longer drift between the document and media resolve paths.
/// </summary>
public enum ShareState
{
    Ok,
    NotFound,
    Revoked,
    Expired,
}

/// <summary>
/// Discriminated outcome of <see cref="ShareLinkResolver.ResolveAsync"/>. A
/// non-<see cref="ShareState.Ok"/> state carries a null <see cref="Share"/>;
/// <see cref="ShareState.Ok"/> always carries one.
/// </summary>
public readonly record struct ShareResolution(ShareState State, ResolvedShare? Share)
{
    public static ShareResolution NotFound() => new(ShareState.NotFound, null);
    public static ShareResolution Revoked() => new(ShareState.Revoked, null);
    public static ShareResolution Expired() => new(ShareState.Expired, null);
    public static ShareResolution Ok(ResolvedShare s) => new(ShareState.Ok, s);
}
