using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TokenHash).HasMaxLength(100).IsRequired();
        builder.Property(s => s.TokenPrefix).HasMaxLength(16).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);

        builder.HasIndex(s => s.TokenHash).IsUnique();
        builder.HasIndex(s => s.DocumentId);
        builder.HasIndex(s => s.RepositoryId);

        builder.HasOne(s => s.Repository)
            .WithMany()
            .HasForeignKey(s => s.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Document)
            .WithMany()
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Revision)
            .WithMany()
            .HasForeignKey(s => s.RevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.CreatedBy)
            .WithMany()
            .HasForeignKey(s => s.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.RevokedBy)
            .WithMany()
            .HasForeignKey(s => s.RevokedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
