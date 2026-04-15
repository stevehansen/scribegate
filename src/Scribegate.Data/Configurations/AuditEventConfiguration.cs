using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ActorUsername).HasMaxLength(100);
        builder.Property(e => e.TargetType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Details).HasMaxLength(8000);
        builder.Property(e => e.IpAddress).HasMaxLength(45);

        builder.HasIndex(e => e.EventType);
        builder.HasIndex(e => e.ActorId);
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => e.CreatedAt);
    }
}
