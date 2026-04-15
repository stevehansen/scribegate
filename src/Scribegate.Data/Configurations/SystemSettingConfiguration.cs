using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(4000).IsRequired();
    }
}
