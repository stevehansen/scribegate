using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class RevisionConfiguration : IEntityTypeConfiguration<Revision>
{
    public void Configure(EntityTypeBuilder<Revision> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Content).IsRequired();
        builder.Property(r => r.Message).HasMaxLength(500).IsRequired();

        builder.HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ParentRevision)
            .WithMany()
            .HasForeignKey(r => r.ParentRevisionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => r.DocumentId);
    }
}
