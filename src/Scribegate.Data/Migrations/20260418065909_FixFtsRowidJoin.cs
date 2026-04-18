using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scribegate.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixFtsRowidJoin : Migration
    {
        // The original AddFullTextSearch migration had two problems:
        //   1. DocumentId was UNINDEXED on a `content=''` + `contentless_delete=1`
        //      FTS5 table. Contentless FTS tables do not retain any column data
        //      (even UNINDEXED ones), so DocumentId always read back as NULL and
        //      every `JOIN Documents d ON d.Id = fts.DocumentId` returned zero
        //      rows.
        //   2. `snippet()` / `highlight()` cannot reconstruct output on a
        //      contentless table because the source text isn't stored, so the
        //      /api/v1/search response included NULL snippets.
        // This migration drops `content=''` so FTS5 keeps its own copy of
        // Content (storage cost: ~2x the markdown, acceptable for a search
        // index), rebuilds the triggers to link via Documents.rowid, and
        // switches the search query over to rowid joins.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `suppressTransaction: true` keeps the DROP/CREATE of the FTS5
            // virtual table out of the per-migration transaction. With the
            // default transactional behavior EF Core's SQLite provider can
            // swallow the schema change silently — the migration records as
            // applied but `sqlite_master` still shows the old table.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_insert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_update;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DocumentFts;");

            migrationBuilder.Sql("CREATE VIRTUAL TABLE DocumentFts USING fts5(Content);");

            migrationBuilder.Sql("""
                INSERT INTO DocumentFts(rowid, Content)
                SELECT d.rowid, r.Content
                FROM Documents d
                JOIN Revisions r ON r.Id = d.CurrentRevisionId
                WHERE d.CurrentRevisionId IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_fts_update
                AFTER UPDATE OF CurrentRevisionId ON Documents
                WHEN NEW.CurrentRevisionId IS NOT NULL
                BEGIN
                    DELETE FROM DocumentFts WHERE rowid = NEW.rowid;
                    INSERT INTO DocumentFts(rowid, Content)
                    SELECT NEW.rowid, r.Content
                    FROM Revisions r WHERE r.Id = NEW.CurrentRevisionId;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_fts_insert
                AFTER INSERT ON Documents
                WHEN NEW.CurrentRevisionId IS NOT NULL
                BEGIN
                    INSERT INTO DocumentFts(rowid, Content)
                    SELECT NEW.rowid, r.Content
                    FROM Revisions r WHERE r.Id = NEW.CurrentRevisionId;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_fts_delete
                BEFORE DELETE ON Documents
                BEGIN
                    DELETE FROM DocumentFts WHERE rowid = OLD.rowid;
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_insert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_document_fts_update;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DocumentFts;");

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE DocumentFts USING fts5(
                    Content,
                    DocumentId UNINDEXED,
                    content='',
                    contentless_delete=1
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO DocumentFts(rowid, Content, DocumentId)
                SELECT d.rowid, r.Content, d.Id
                FROM Documents d
                JOIN Revisions r ON r.Id = d.CurrentRevisionId
                WHERE d.CurrentRevisionId IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_fts_update
                AFTER UPDATE OF CurrentRevisionId ON Documents
                WHEN NEW.CurrentRevisionId IS NOT NULL
                BEGIN
                    DELETE FROM DocumentFts WHERE DocumentId = NEW.Id;
                    INSERT INTO DocumentFts(Content, DocumentId)
                    SELECT r.Content, NEW.Id
                    FROM Revisions r WHERE r.Id = NEW.CurrentRevisionId;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_fts_insert
                AFTER INSERT ON Documents
                WHEN NEW.CurrentRevisionId IS NOT NULL
                BEGIN
                    INSERT INTO DocumentFts(Content, DocumentId)
                    SELECT r.Content, NEW.Id
                    FROM Revisions r WHERE r.Id = NEW.CurrentRevisionId;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_document_fts_delete
                BEFORE DELETE ON Documents
                BEGIN
                    DELETE FROM DocumentFts WHERE DocumentId = OLD.Id;
                END;
                """);
        }
    }
}
