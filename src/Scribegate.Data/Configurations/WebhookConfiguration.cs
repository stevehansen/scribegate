using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class WebhookConfiguration : IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Webhook> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Url).HasMaxLength(2000).IsRequired();
        builder.Property(w => w.Secret).HasMaxLength(128).IsRequired();
        builder.Property(w => w.Events).HasMaxLength(2000).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(500);

        builder.HasIndex(w => w.RepositoryId);
        builder.HasIndex(w => new { w.Enabled, w.RepositoryId });

        builder.HasOne(w => w.Repository)
            .WithMany()
            .HasForeignKey(w => w.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.CreatedBy)
            .WithMany()
            .HasForeignKey(w => w.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
