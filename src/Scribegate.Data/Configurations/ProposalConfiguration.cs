using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.ProposedContent).IsRequired();
        builder.Property(p => p.ProposedPath).HasMaxLength(500);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => new { p.RepositoryId, p.Status });
        builder.HasIndex(p => p.DocumentId);
        builder.HasIndex(p => p.CreatedById);

        builder.HasOne(p => p.Repository)
            .WithMany()
            .HasForeignKey(p => p.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Document)
            .WithMany()
            .HasForeignKey(p => p.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.BaseRevision)
            .WithMany()
            .HasForeignKey(p => p.BaseRevisionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.CreatedBy)
            .WithMany()
            .HasForeignKey(p => p.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ResolvedBy)
            .WithMany()
            .HasForeignKey(p => p.ResolvedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
