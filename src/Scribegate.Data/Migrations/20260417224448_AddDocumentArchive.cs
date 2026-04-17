using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scribegate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedById",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_RepositoryId_IsArchived",
                table: "Documents",
                columns: new[] { "RepositoryId", "IsArchived" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_RepositoryId_IsArchived",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ArchivedById",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Documents");
        }
    }
}
