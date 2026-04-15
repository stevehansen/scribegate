using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scribegate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAbusePreventionFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TosAcceptedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_Status",
                table: "ContentReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReporterUserId_TargetType_TargetId",
                table: "ContentReports",
                columns: new[] { "ReporterUserId", "TargetType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentReports");

            migrationBuilder.DropColumn(
                name: "TosAcceptedAt",
                table: "Users");
        }
    }
}
