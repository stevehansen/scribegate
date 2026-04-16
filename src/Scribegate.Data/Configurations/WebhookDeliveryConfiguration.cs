using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scribegate.Core.Entities;

namespace Scribegate.Data.Configurations;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.EventType).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Payload).IsRequired();
        builder.Property(d => d.Error).HasMaxLength(1000);
        builder.Property(d => d.ResponseBody).HasMaxLength(2000);

        builder.HasIndex(d => new { d.WebhookId, d.CreatedAt });

        builder.HasOne(d => d.Webhook)
            .WithMany()
            .HasForeignKey(d => d.WebhookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
