using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Verdict).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Body).HasMaxLength(4000);

        builder.HasIndex(r => r.ProposalId);

        builder.HasOne(r => r.Proposal)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
