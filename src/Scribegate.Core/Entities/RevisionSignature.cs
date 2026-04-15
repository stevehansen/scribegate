namespace Scribegate.Core.Entities;

public class RevisionSignature
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public required string Algorithm { get; set; }
    public required string PublicKeyId { get; set; }
    public required string Signature { get; set; }
    public required string ContentHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Revision Revision { get; set; } = null!;
}
