using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Threadly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentaryReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "Commentaries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commentaries_ParentId_CreatedAtUtc_Id",
                table: "Commentaries",
                columns: new[] { "ParentId", "CreatedAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Commentaries_ParentId_NotSelf",
                table: "Commentaries",
                sql: "[ParentId] <> [Id]");

            migrationBuilder.AddForeignKey(
                name: "FK_Commentaries_Commentaries_ParentId",
                table: "Commentaries",
                column: "ParentId",
                principalTable: "Commentaries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Commentaries_Commentaries_ParentId",
                table: "Commentaries");

            migrationBuilder.DropIndex(
                name: "IX_Commentaries_ParentId_CreatedAtUtc_Id",
                table: "Commentaries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Commentaries_ParentId_NotSelf",
                table: "Commentaries");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Commentaries");
        }
    }
}
