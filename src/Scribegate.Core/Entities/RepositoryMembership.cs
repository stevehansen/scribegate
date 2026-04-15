using Scribegate.Core.Enums;

namespace Scribegate.Core.Entities;

public class RepositoryMembership
{
    public Guid UserId { get; set; }
    public Guid RepositoryId { get; set; }
    public RepositoryRole Role { get; set; }

    public User User { get; set; } = null!;
    public Repository Repository { get; set; } = null!;
}
