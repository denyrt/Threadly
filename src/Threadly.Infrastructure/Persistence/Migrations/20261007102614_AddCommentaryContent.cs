using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Threadly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentaryContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "Commentaries",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            // Preserve development rows without retaining the old API contract.
            migrationBuilder.Sql("""
                UPDATE [Commentaries]
                SET [Content] = N'[{"type":"text","html":"' +
                    STRING_ESCAPE(REPLACE(REPLACE(REPLACE(CAST([Text] AS nvarchar(max)), N'&', N'&amp;'), N'<', N'&lt;'), N'>', N'&gt;'), 'json') + N'"}]';
                """);
            migrationBuilder.DropColumn(name: "Text", table: "Commentaries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Commentaries_Content_Json",
                table: "Commentaries",
                sql: "ISJSON([Content], ARRAY) = 1 AND DATALENGTH([Content]) <= 524288");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Commentaries_Content_Json",
                table: "Commentaries");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "Commentaries");

            migrationBuilder.AddColumn<string>(
                name: "Text",
                table: "Commentaries",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }
    }
}
