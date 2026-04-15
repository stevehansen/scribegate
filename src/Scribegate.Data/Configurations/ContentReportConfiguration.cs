using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class ContentReportConfiguration : IEntityTypeConfiguration<ContentReport>
{
    public void Configure(EntityTypeBuilder<ContentReport> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TargetType).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.ReviewNotes).HasMaxLength(2000);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(50);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => new { r.ReporterUserId, r.TargetType, r.TargetId });
    }
}
