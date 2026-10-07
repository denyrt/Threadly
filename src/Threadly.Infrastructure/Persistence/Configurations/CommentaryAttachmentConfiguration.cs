using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Threadly.Domain.Commentaries;

namespace Threadly.Infrastructure.Persistence.Configurations;

internal sealed class CommentaryAttachmentConfiguration : IEntityTypeConfiguration<CommentaryAttachment>
{
    public void Configure(EntityTypeBuilder<CommentaryAttachment> builder)
    {
        builder.ToTable("CommentaryAttachments", table =>
        {
            table.HasCheckConstraint("CK_CommentaryAttachments_Position", "[Position] >= 0 AND [Position] < 10");
            table.HasCheckConstraint("CK_CommentaryAttachments_Content", "[Size] > 0 AND [Size] <= 2097152 AND DATALENGTH([Content]) = [Size]");
            table.HasCheckConstraint("CK_CommentaryAttachments_Type",
                "([ContentType] = 'text/plain' AND [Width] IS NULL AND [Height] IS NULL AND [Size] <= 102400) OR "
                + "([ContentType] IN ('image/jpeg', 'image/png', 'image/gif') AND [Width] IS NOT NULL AND [Height] IS NOT NULL "
                + "AND [Width] BETWEEN 1 AND 320 AND [Height] BETWEEN 1 AND 240)");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.HasOne<Commentary>().WithMany(value => value.Attachments).HasForeignKey(value => value.CommentaryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.CommentaryId, value.Position }).IsUnique();
        builder.Property(value => value.FileName).HasMaxLength(CommentaryAttachment.MaxFileNameLength).IsRequired();
        builder.Property(value => value.ContentType).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(value => value.Content).HasColumnType("varbinary(max)").IsRequired();
    }
}
