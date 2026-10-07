using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence.Configurations;

internal sealed class CommentaryConfiguration : IEntityTypeConfiguration<Commentary>
{
    public void Configure(EntityTypeBuilder<Commentary> builder)
    {
        builder.ToTable("Commentaries", table =>
            table.HasCheckConstraint("CK_Commentaries_ParentId_NotSelf", "[ParentId] <> [Id]"));

        builder.HasKey(commentary => commentary.Id);
        builder.Property(commentary => commentary.Id).ValueGeneratedNever();

        builder.Property(commentary => commentary.ParentId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.HasOne<Commentary>().WithMany().HasForeignKey(commentary => commentary.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(commentary => new { commentary.ParentId, commentary.CreatedAtUtc, commentary.Id });
        // Include the IS NULL predicate column so SQL Server can use these filtered indexes.
        builder.HasIndex(commentary => new { commentary.Username, commentary.Id })
            .HasFilter("[ParentId] IS NULL")
            .IncludeProperties(commentary => commentary.ParentId);
        builder.HasIndex(commentary => new { commentary.Email, commentary.Id })
            .HasFilter("[ParentId] IS NULL")
            .IncludeProperties(commentary => commentary.ParentId);

        builder.Property(commentary => commentary.Username)
            .IsRequired()
            .HasMaxLength(Commentary.MaxUsernameLength);

        builder.Property(commentary => commentary.Email)
            .IsRequired()
            .HasMaxLength(Commentary.MaxEmailLength);

        builder.Property(commentary => commentary.Content)
            .HasConversion(value => ContentJson.Serialize(value), value => ContentJson.Deserialize(value),
                new ValueComparer<IReadOnlyList<TextContentBlock>>(
                    (left, right) => left!.SequenceEqual(right!),
                    value => value.Aggregate(0, (hash, block) => HashCode.Combine(hash, block)),
                    value => value.ToArray()))
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.ToTable("Commentaries", table => table.HasCheckConstraint("CK_Commentaries_Content_Json",
            "ISJSON([Content], ARRAY) = 1 AND DATALENGTH([Content]) <= 524288"));

        builder.Property(commentary => commentary.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
