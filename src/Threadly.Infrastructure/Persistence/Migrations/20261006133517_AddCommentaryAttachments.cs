using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Threadly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentaryAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentaryAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommentaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentaryAttachments", x => x.Id);
                    table.CheckConstraint("CK_CommentaryAttachments_Content", "[Size] > 0 AND [Size] <= 2097152 AND DATALENGTH([Content]) = [Size]");
                    table.CheckConstraint("CK_CommentaryAttachments_Position", "[Position] >= 0 AND [Position] < 10");
                    table.CheckConstraint("CK_CommentaryAttachments_Type", "([ContentType] = 'text/plain' AND [Width] IS NULL AND [Height] IS NULL AND [Size] <= 102400) OR ([ContentType] IN ('image/jpeg', 'image/png', 'image/gif') AND [Width] IS NOT NULL AND [Height] IS NOT NULL AND [Width] BETWEEN 1 AND 320 AND [Height] BETWEEN 1 AND 240)");
                    table.ForeignKey(
                        name: "FK_CommentaryAttachments_Commentaries_CommentaryId",
                        column: x => x.CommentaryId,
                        principalTable: "Commentaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentaryAttachments_CommentaryId_Position",
                table: "CommentaryAttachments",
                columns: new[] { "CommentaryId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentaryAttachments");
        }
    }
}
