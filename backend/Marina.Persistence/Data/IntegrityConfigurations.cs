using Marina.Domain.Catalog;
using Marina.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marina.Persistence.Data;

public sealed class ReferenceIntegrityConfiguration : IEntityTypeConfiguration<Brand>,
    IEntityTypeConfiguration<ColorOption>, IEntityTypeConfiguration<SizeOption>,
    IEntityTypeConfiguration<ColorTranslation>, IEntityTypeConfiguration<SizeTranslation>
{
    public void Configure(EntityTypeBuilder<Brand> b)
    {
        b.Property(x => x.Slug).HasMaxLength(180);
        b.HasIndex(x => x.Slug).IsUnique();
    }
    public void Configure(EntityTypeBuilder<ColorOption> b)
    {
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.Hex).HasMaxLength(9);
        b.HasIndex(x => x.Code).IsUnique();
    }
    public void Configure(EntityTypeBuilder<SizeOption> b)
    {
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasIndex(x => x.Code).IsUnique();
    }
    public void Configure(EntityTypeBuilder<ColorTranslation> b)
    {
        b.Property(x => x.Culture).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(160);
        b.HasIndex(x => new { x.ColorOptionId, x.Culture }).IsUnique();
    }
    public void Configure(EntityTypeBuilder<SizeTranslation> b)
    {
        b.Property(x => x.Culture).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(160);
        b.HasIndex(x => new { x.SizeOptionId, x.Culture }).IsUnique();
    }
}
