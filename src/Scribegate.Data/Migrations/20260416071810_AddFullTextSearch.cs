using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scribegate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create FTS5 virtual table for document content search
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE IF NOT EXISTS DocumentFts USING fts5(
                    Content,
                    DocumentId UNINDEXED,
                    content='',
                    contentless_delete=1
                );
                """);

            // Populate FTS index from existing revisions (current content only)
            migrationBuilder.Sql("""
                INSERT INTO DocumentFts(rowid, Content, DocumentId)
                SELECT d.rowid, r.Content, d.Id
                FROM Documents d
                JOIN Revisions r ON r.Id = d.CurrentRevisionId
                WHERE d.CurrentRevisionId IS NOT NULL;
                """);

            // Trigger: auto-update FTS when a document's current revision changes
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_document_fts_update
                AFTER UPDATE OF CurrentRevisionId ON Documents
                WHEN NEW.CurrentRevisionId IS NOT NULL
                BEGIN
                    DELETE FROM DocumentFts WHERE DocumentId = NEW.Id;
                    INSERT INTO DocumentFts(Content, DocumentId)
                    SELECT r.Content, NEW.Id
                    FROM Revisions r WHERE r.Id = NEW.CurrentRevisionId;
                END;
                """);

            // Trigger: insert into FTS when first revision is set
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_document_fts_insert
                AFTER INSERT ON Documents
                WHEN NEW.CurrentRevisionId IS NOT NULL
                BEGIN
                    INSERT INTO DocumentFts(Content, DocumentId)
                    SELECT r.Content, NEW.Id
                    FROM Revisions r WHERE r.Id = NEW.CurrentRevisionId;
                END;
                """);

            // Trigger: remove from FTS on document delete
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_document_fts_delete
                BEFORE DELETE ON Documents
                BEGIN
                    DELETE FROM DocumentFts WHERE DocumentId = OLD.Id;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_insert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_update;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DocumentFts;");
        }
    }
}
