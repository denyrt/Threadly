using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Threadly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRootCommentarySorting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Required for filtered indexes, including when applying the generated SQL script.
            migrationBuilder.Sql("""
                SET ANSI_NULLS ON;
                SET ANSI_PADDING ON;
                SET ANSI_WARNINGS ON;
                SET ARITHABORT ON;
                SET CONCAT_NULL_YIELDS_NULL ON;
                SET QUOTED_IDENTIFIER ON;
                SET NUMERIC_ROUNDABORT OFF;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Commentaries_Email_Id",
                table: "Commentaries",
                columns: new[] { "Email", "Id" },
                filter: "[ParentId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Commentaries_Username_Id",
                table: "Commentaries",
                columns: new[] { "Username", "Id" },
                filter: "[ParentId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ParentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Commentaries_Email_Id",
                table: "Commentaries");

            migrationBuilder.DropIndex(
                name: "IX_Commentaries_Username_Id",
                table: "Commentaries");
        }
    }
}
