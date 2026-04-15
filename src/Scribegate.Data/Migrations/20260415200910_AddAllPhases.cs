using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scribegate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllPhases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProvider",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FrontmatterJson",
                table: "Documents",
                type: "TEXT",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ProposedContent = table.Column<string>(type: "TEXT", nullable: false),
                    ProposedPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BaseRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Proposals_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Proposals_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Proposals_Revisions_BaseRevisionId",
                        column: x => x.BaseRevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Proposals_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Proposals_Users_ResolvedById",
                        column: x => x.ResolvedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RevisionSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PublicKeyId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Signature = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevisionSignatures_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    LineReference = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_Proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "Proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Verdict = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_Proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "Proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reviews_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId",
                table: "AuditEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CreatedAt",
                table: "AuditEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventType",
                table: "AuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TargetType_TargetId",
                table: "AuditEvents",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_CreatedById",
                table: "Comments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentCommentId",
                table: "Comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ProposalId",
                table: "Comments",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_BaseRevisionId",
                table: "Proposals",
                column: "BaseRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_CreatedById",
                table: "Proposals",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_DocumentId",
                table: "Proposals",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_RepositoryId_Status",
                table: "Proposals",
                columns: new[] { "RepositoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_ResolvedById",
                table: "Proposals",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_CreatedById",
                table: "Reviews",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ProposalId",
                table: "Reviews",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionSignatures_RevisionId",
                table: "RevisionSignatures",
                column: "RevisionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "RevisionSignatures");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "Proposals");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExternalProvider",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FrontmatterJson",
                table: "Documents");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
