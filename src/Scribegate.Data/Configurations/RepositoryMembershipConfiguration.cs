using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class RepositoryMembershipConfiguration : IEntityTypeConfiguration<RepositoryMembership>
{
    public void Configure(EntityTypeBuilder<RepositoryMembership> builder)
    {
        builder.HasKey(m => new { m.UserId, m.RepositoryId });

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
