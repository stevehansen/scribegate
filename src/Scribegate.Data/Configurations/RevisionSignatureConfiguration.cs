using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class RevisionSignatureConfiguration : IEntityTypeConfiguration<RevisionSignature>
{
    public void Configure(EntityTypeBuilder<RevisionSignature> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Algorithm).HasMaxLength(50).IsRequired();
        builder.Property(s => s.PublicKeyId).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Signature).HasMaxLength(500).IsRequired();
        builder.Property(s => s.ContentHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(s => s.RevisionId).IsUnique();

        builder.HasOne(s => s.Revision)
            .WithOne()
            .HasForeignKey<RevisionSignature>(s => s.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
