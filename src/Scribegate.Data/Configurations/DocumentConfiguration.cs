using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Path).HasMaxLength(500).IsRequired();

        builder.Property(d => d.FrontmatterJson).HasMaxLength(8000);

        builder.HasIndex(d => new { d.RepositoryId, d.Path }).IsUnique();
        builder.HasIndex(d => new { d.RepositoryId, d.IsArchived });

        builder.HasOne(d => d.CurrentRevision)
            .WithOne()
            .HasForeignKey<Document>(d => d.CurrentRevisionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.CreatedBy)
            .WithMany()
            .HasForeignKey(d => d.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Revisions)
            .WithOne(r => r.Document)
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
