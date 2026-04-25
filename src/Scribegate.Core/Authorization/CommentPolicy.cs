using Scribegate.Core.Entities;

namespace Scribegate.Core.Authorization;

/// <summary>
/// Domain-authorization rules for <see cref="Comment"/> verbs. Edits are
/// creator-only; deletes allow either the creator or a global admin.
/// </summary>
public static class CommentPolicy
{
    public static PolicyResult CanEdit(Comment comment, User actor)
    {
        if (comment.CreatedById != actor.Id)
            return PolicyResult.Forbid("FORBIDDEN", "You can only edit your own comments.");
        return PolicyResult.Allow();
    }

    public static PolicyResult CanDelete(Comment comment, User actor)
    {
        if (comment.CreatedById != actor.Id && !actor.IsAdmin)
            return PolicyResult.Forbid("FORBIDDEN", "You can only delete your own comments.");
        return PolicyResult.Allow();
    }
}
