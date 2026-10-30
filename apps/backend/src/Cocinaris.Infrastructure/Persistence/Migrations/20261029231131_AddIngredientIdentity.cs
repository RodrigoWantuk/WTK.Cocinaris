using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PLAN-0055 / ADR-0010. The deploy script creates both extensions as the database
            // superuser before migrations run; IF NOT EXISTS makes these statements no-ops there and
            // creates them in development and test databases, where the role is a superuser.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.EnsureSchema(
                name: "ingredients");

            migrationBuilder.CreateTable(
                name: "catalog_items",
                schema: "ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NamesJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_state",
                schema: "ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Evaluation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_state", x => x.Id);
                    table.CheckConstraint("ck_ingredient_catalog_state_single", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "resolution_queue",
                schema: "ingredients",
                columns: table => new
                {
                    NormalizedText = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExactText = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Evaluation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BestIngredientId = table.Column<int>(type: "integer", nullable: true),
                    BestScore = table.Column<double>(type: "double precision", nullable: true),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resolution_queue", x => x.NormalizedText);
                    table.CheckConstraint("ck_ingredient_resolution_state", "\"State\" IN ('Pending', 'Working', 'Unresolved')");
                });

            migrationBuilder.CreateTable(
                name: "aliases",
                schema: "ingredients",
                columns: table => new
                {
                    NormalizedText = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IngredientId = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Evaluation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aliases", x => x.NormalizedText);
                    table.CheckConstraint("ck_ingredient_aliases_method", "\"Method\" IN ('Catalog', 'Trigram', 'Embedding')");
                    table.ForeignKey(
                        name: "FK_aliases_catalog_items_IngredientId",
                        column: x => x.IngredientId,
                        principalSchema: "ingredients",
                        principalTable: "catalog_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aliases_IngredientId",
                schema: "ingredients",
                table: "aliases",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_items_Slug",
                schema: "ingredients",
                table: "catalog_items",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resolution_queue_State_NextAttemptAt",
                schema: "ingredients",
                table: "resolution_queue",
                columns: new[] { "State", "NextAttemptAt" });

            // Trigram search over curated names (IngredientResolutionService step 2).
            migrationBuilder.Sql("""CREATE INDEX "IX_aliases_NormalizedText_trgm" ON ingredients.aliases USING gin ("NormalizedText" gin_trgm_ops);""");

            // Embedding cache by exact text. The key is SHA-256 of the vector space, role, instruction
            // version, and exact text; the text itself is not stored. The vector length is not fixed in
            // the type so a dimension change needs no migration (the space string separates them).
            migrationBuilder.Sql("""
                CREATE TABLE ingredients.text_embeddings (
                    "Key" character varying(64) NOT NULL PRIMARY KEY,
                    "Space" character varying(200) NOT NULL,
                    "Embedding" vector NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL
                );
                """);

            // One vector per catalog ingredient and space. About 150 rows, so the nearest-neighbour
            // query scans them exactly; an HNSW index (which needs a fixed dimension) is added when a
            // larger table such as the shared recipe library needs one.
            migrationBuilder.Sql("""
                CREATE TABLE ingredients.catalog_vectors (
                    "IngredientId" integer NOT NULL REFERENCES ingredients.catalog_items ("Id") ON DELETE RESTRICT,
                    "Space" character varying(200) NOT NULL,
                    "DocumentKey" character varying(64) NOT NULL,
                    "Embedding" vector NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    PRIMARY KEY ("IngredientId", "Space")
                );
                """);

            // The background-maintenance principal (SystemPrincipals.Maintenance) that owns provider
            // calls no person triggered. Its issuer is not an OIDC issuer, so nobody can sign in as it.
            migrationBuilder.Sql("""
                INSERT INTO identity.users ("Id", "Issuer", "Subject", "CreatedAt")
                VALUES ('00000000-0000-0000-0000-00000000c0c1', 'urn:cocinaris:system', 'maintenance', now())
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS ingredients.catalog_vectors;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS ingredients.text_embeddings;");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS ingredients."IX_aliases_NormalizedText_trgm";""");
            // The principal stays while ledger rows reference it; the extensions stay for other users.
            migrationBuilder.Sql("""
                DELETE FROM identity.users u WHERE u."Id" = '00000000-0000-0000-0000-00000000c0c1'
                AND NOT EXISTS (SELECT 1 FROM ai.usage_ledger l WHERE l."OwnerUserId" = u."Id");
                """);

            migrationBuilder.DropTable(
                name: "aliases",
                schema: "ingredients");

            migrationBuilder.DropTable(
                name: "catalog_state",
                schema: "ingredients");

            migrationBuilder.DropTable(
                name: "resolution_queue",
                schema: "ingredients");

            migrationBuilder.DropTable(
                name: "catalog_items",
                schema: "ingredients");
        }
    }
}
