using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.FileName).HasMaxLength(500).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.StoragePath).HasMaxLength(1000).IsRequired();

        builder.HasOne(m => m.Repository)
            .WithMany()
            .HasForeignKey(m => m.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.UploadedBy)
            .WithMany()
            .HasForeignKey(m => m.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.RepositoryId);
    }
}
