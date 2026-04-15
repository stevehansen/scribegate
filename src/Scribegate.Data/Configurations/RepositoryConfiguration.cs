using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Slug).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Visibility).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => r.Slug).IsUnique();

        builder.HasMany(r => r.Documents)
            .WithOne(d => d.Repository)
            .HasForeignKey(d => d.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Memberships)
            .WithOne(m => m.Repository)
            .HasForeignKey(m => m.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
