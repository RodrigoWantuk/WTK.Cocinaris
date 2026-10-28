using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TightenExpirationProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_expiration_provenance",
                schema: "inventory",
                table: "lots");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_expiration_provenance",
                schema: "inventory",
                table: "lots",
                sql: "(\"PrintedExpirationDate\" IS NULL AND \"ExpirationProvenance\" IS NULL) OR (\"PrintedExpirationDate\" IS NOT NULL AND \"ExpirationProvenance\" IS NOT NULL AND \"ExpirationProvenance\" = 'UserEntered')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_expiration_provenance",
                schema: "inventory",
                table: "lots");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_expiration_provenance",
                schema: "inventory",
                table: "lots",
                sql: "(\"PrintedExpirationDate\" IS NULL AND \"ExpirationProvenance\" IS NULL) OR (\"PrintedExpirationDate\" IS NOT NULL AND \"ExpirationProvenance\" = 'UserEntered')");
        }
    }
}
