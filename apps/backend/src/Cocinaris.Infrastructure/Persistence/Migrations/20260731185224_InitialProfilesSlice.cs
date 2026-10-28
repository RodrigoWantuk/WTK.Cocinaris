using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProfilesSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "profiles");

            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "profiles",
                columns: table => new
                {
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DisplayNamePresence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultAdultCount = table.Column<int>(type: "integer", nullable: true),
                    DefaultAdultCountPresence = table.Column<string>(type: "text", nullable: false),
                    DefaultChildCount = table.Column<int>(type: "integer", nullable: true),
                    DefaultChildCountPresence = table.Column<string>(type: "text", nullable: false),
                    DefaultServingCount = table.Column<int>(type: "integer", nullable: true),
                    DefaultServingCountPresence = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    LanguagePresence = table.Column<string>(type: "text", nullable: false),
                    Region = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    RegionPresence = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CurrencyPresence = table.Column<string>(type: "text", nullable: false),
                    MeasurementSystem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MeasurementSystemPresence = table.Column<string>(type: "text", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimeZonePresence = table.Column<string>(type: "text", nullable: false),
                    PlanningCadence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PlanningCadencePresence = table.Column<string>(type: "text", nullable: false),
                    ShoppingCadence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShoppingCadencePresence = table.Column<string>(type: "text", nullable: false),
                    OverallSkill = table.Column<string>(type: "text", nullable: true),
                    OverallSkillPresence = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<string>(type: "text", nullable: true),
                    ConfidencePresence = table.Column<string>(type: "text", nullable: false),
                    PreferredInstructionDetail = table.Column<string>(type: "text", nullable: true),
                    PreferredInstructionDetailPresence = table.Column<string>(type: "text", nullable: false),
                    OrdinaryPrepMinutes = table.Column<int>(type: "integer", nullable: true),
                    OrdinaryPrepMinutesPresence = table.Column<string>(type: "text", nullable: false),
                    ExceptionalPrepMinutes = table.Column<int>(type: "integer", nullable: true),
                    ExceptionalPrepMinutesPresence = table.Column<string>(type: "text", nullable: false),
                    EffortTolerance = table.Column<string>(type: "text", nullable: true),
                    EffortTolerancePresence = table.Column<string>(type: "text", nullable: false),
                    CleanupTolerance = table.Column<string>(type: "text", nullable: true),
                    CleanupTolerancePresence = table.Column<string>(type: "text", nullable: false),
                    RepeatMealPreference = table.Column<string>(type: "text", nullable: true),
                    RepeatMealPreferencePresence = table.Column<string>(type: "text", nullable: false),
                    ReheatingPreference = table.Column<string>(type: "text", nullable: true),
                    ReheatingPreferencePresence = table.Column<string>(type: "text", nullable: false),
                    LeftoverPreference = table.Column<string>(type: "text", nullable: true),
                    LeftoverPreferencePresence = table.Column<string>(type: "text", nullable: false),
                    FreezingPreference = table.Column<string>(type: "text", nullable: true),
                    FreezingPreferencePresence = table.Column<string>(type: "text", nullable: false),
                    AdultDeclared = table.Column<bool>(type: "boolean", nullable: true),
                    TermsVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PrivacyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TermsAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.OwnerUserId);
                    table.CheckConstraint("ck_profiles_currency", "\"Currency\" IS NULL OR \"Currency\" IN ('USD', 'BRL', 'EUR')");
                    table.CheckConstraint("ck_profiles_default_adult_count", "\"DefaultAdultCount\" IS NULL OR (\"DefaultAdultCount\" BETWEEN 1 AND 20)");
                    table.CheckConstraint("ck_profiles_default_child_count", "\"DefaultChildCount\" IS NULL OR (\"DefaultChildCount\" BETWEEN 0 AND 20)");
                    table.CheckConstraint("ck_profiles_default_serving_count", "\"DefaultServingCount\" IS NULL OR (\"DefaultServingCount\" BETWEEN 1 AND 30)");
                    table.CheckConstraint("ck_profiles_language", "\"Language\" IS NULL OR \"Language\" IN ('en', 'pt-BR', 'es')");
                    table.CheckConstraint("ck_profiles_measurement_system", "\"MeasurementSystem\" IS NULL OR \"MeasurementSystem\" IN ('Metric', 'UsCustomary')");
                    table.CheckConstraint("ck_profiles_region", "\"Region\" IS NULL OR \"Region\" IN ('US', 'BR', 'ES')");
                    table.ForeignKey(
                        name: "FK_user_profiles_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "change_history",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionChanged = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FieldCodesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_change_history_user_profiles_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "profiles",
                        principalTable: "user_profiles",
                        principalColumn: "OwnerUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "equipment_entries",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StableCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Capacity = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    CapacityUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ConstraintNote = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment_entries", x => x.Id);
                    table.CheckConstraint("ck_equipment_capacity", "\"Capacity\" IS NULL OR \"Capacity\" >= 0");
                    table.ForeignKey(
                        name: "FK_equipment_entries_user_profiles_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "profiles",
                        principalTable: "user_profiles",
                        principalColumn: "OwnerUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordered_code_entries",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StableCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordered_code_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ordered_code_entries_user_profiles_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "profiles",
                        principalTable: "user_profiles",
                        principalColumn: "OwnerUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preference_entries",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StableCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Presence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preference_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_preference_entries_user_profiles_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "profiles",
                        principalTable: "user_profiles",
                        principalColumn: "OwnerUserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_change_history_OwnerUserId_OccurredAt",
                schema: "profiles",
                table: "change_history",
                columns: new[] { "OwnerUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_equipment_entries_OwnerUserId_StableCode",
                schema: "profiles",
                table: "equipment_entries",
                columns: new[] { "OwnerUserId", "StableCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ordered_code_entries_OwnerUserId_ListName_SortOrder",
                schema: "profiles",
                table: "ordered_code_entries",
                columns: new[] { "OwnerUserId", "ListName", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_preference_entries_OwnerUserId_Category_StableCode",
                schema: "profiles",
                table: "preference_entries",
                columns: new[] { "OwnerUserId", "Category", "StableCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_ConcurrencyToken",
                schema: "profiles",
                table: "user_profiles",
                column: "ConcurrencyToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_history",
                schema: "profiles");

            migrationBuilder.DropTable(
                name: "equipment_entries",
                schema: "profiles");

            migrationBuilder.DropTable(
                name: "ordered_code_entries",
                schema: "profiles");

            migrationBuilder.DropTable(
                name: "preference_entries",
                schema: "profiles");

            migrationBuilder.DropTable(
                name: "user_profiles",
                schema: "profiles");
        }
    }
}
