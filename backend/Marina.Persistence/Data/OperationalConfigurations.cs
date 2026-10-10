using Marina.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marina.Persistence.Data;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Key).HasMaxLength(120);
        b.Property(x => x.Value).HasMaxLength(2000);
    }
}
