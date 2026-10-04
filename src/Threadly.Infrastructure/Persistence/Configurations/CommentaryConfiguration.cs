using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence.Configurations;

internal sealed class CommentaryConfiguration : IEntityTypeConfiguration<Commentary>
{
    public void Configure(EntityTypeBuilder<Commentary> builder)
    {
        builder.ToTable("Commentaries");

        builder.HasKey(commentary => commentary.Id);
        builder.Property(commentary => commentary.Id).ValueGeneratedNever();

        builder.Property(commentary => commentary.Username)
            .IsRequired()
            .HasMaxLength(Commentary.MaxUsernameLength);

        builder.Property(commentary => commentary.Email)
            .IsRequired()
            .HasMaxLength(Commentary.MaxEmailLength);

        builder.Property(commentary => commentary.Text)
            .IsRequired()
            .HasMaxLength(Commentary.MaxTextLength);

        builder.Property(commentary => commentary.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
