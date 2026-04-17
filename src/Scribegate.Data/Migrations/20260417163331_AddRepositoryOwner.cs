using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scribegate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_Slug",
                table: "Repositories");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill: assign existing repositories to the earliest admin user.
            // If no admin exists the migration aborts — the non-nullable FK constraint
            // would fail anyway and leaving empty GUIDs would hide the problem.
            migrationBuilder.Sql(@"
                UPDATE Repositories
                SET OwnerId = (
                    SELECT Id FROM Users
                    WHERE IsAdmin = 1
                    ORDER BY CreatedAt ASC
                    LIMIT 1
                )
                WHERE OwnerId = '00000000-0000-0000-0000-000000000000';
            ");

            // Abort if any repository is still unowned (e.g. no admin user exists).
            migrationBuilder.Sql(@"
                SELECT RAISE(ABORT, 'AddRepositoryOwner migration: no admin user found to own existing repositories. Create an admin user first, then reapply migrations.')
                WHERE EXISTS (
                    SELECT 1 FROM Repositories
                    WHERE OwnerId = '00000000-0000-0000-0000-000000000000'
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerId",
                table: "Repositories",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerId_Slug",
                table: "Repositories",
                columns: new[] { "OwnerId", "Slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Users_OwnerId",
                table: "Repositories",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Users_OwnerId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_OwnerId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_OwnerId_Slug",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Repositories");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Slug",
                table: "Repositories",
                column: "Slug",
                unique: true);
        }
    }
}
