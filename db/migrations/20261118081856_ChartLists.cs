using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ehr.Migrations
{
    /// <inheritdoc />
    public partial class ChartLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sensitive",
                schema: "ehr",
                table: "medication_statements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "sensitive",
                schema: "ehr",
                table: "conditions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "code_system",
                schema: "ehr",
                table: "allergy_intolerances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "severity",
                schema: "ehr",
                table: "allergy_intolerances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chart_history",
                schema: "ehr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_chart_history_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "ehr",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chart_history_staff_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalSchema: "ehr",
                        principalTable: "staff_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_codes",
                schema: "ehr",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    system = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    display_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, computedColumnSql: "lower(display)", stored: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sensitive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinical_codes", x => new { x.kind, x.system, x.code });
                    table.CheckConstraint("ck_clinical_codes_category", "category IN ('food', 'medication', 'environment', 'biologic')");
                    table.CheckConstraint("ck_clinical_codes_kind", "kind IN ('condition', 'medication', 'allergen')");
                    table.CheckConstraint("ck_clinical_codes_system", "system IN ('icd-10-cm', 'rxnorm', 'unii')");
                });

            // "No known allergies" was recorded with a SNOMED CT code; no licensed code system is used any more, so it
            // becomes the app's own marker. Forced row-level security would hide every row from this update, so it is
            // lifted for the statement, as the synthetic seed lifts it, and put back before the migration's
            // transaction commits (on a new database it isn't on yet: db/policies turns it on afterwards).
            migrationBuilder.Sql(
                """
                ALTER TABLE ehr.allergy_intolerances NO FORCE ROW LEVEL SECURITY;
                UPDATE ehr.allergy_intolerances SET code = 'no-known-allergy' WHERE code = '716186003';
                DO $$
                BEGIN
                    IF (SELECT relrowsecurity FROM pg_class WHERE oid = 'ehr.allergy_intolerances'::regclass) THEN
                        ALTER TABLE ehr.allergy_intolerances FORCE ROW LEVEL SECURITY;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_allergy_intolerances_severity",
                schema: "ehr",
                table: "allergy_intolerances",
                sql: "severity IN ('mild', 'moderate', 'severe')");

            migrationBuilder.CreateIndex(
                name: "ix_chart_history_changed_by_id",
                schema: "ehr",
                table: "chart_history",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_chart_history_patient_id_table_name_changed_at",
                schema: "ehr",
                table: "chart_history",
                columns: new[] { "patient_id", "table_name", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_codes_kind_display_key",
                schema: "ehr",
                table: "clinical_codes",
                columns: new[] { "kind", "display_key" })
                .Annotation("Npgsql:IndexOperators", new[] { null, "text_pattern_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chart_history",
                schema: "ehr");

            migrationBuilder.DropTable(
                name: "clinical_codes",
                schema: "ehr");

            migrationBuilder.DropCheckConstraint(
                name: "ck_allergy_intolerances_severity",
                schema: "ehr",
                table: "allergy_intolerances");

            migrationBuilder.DropColumn(
                name: "sensitive",
                schema: "ehr",
                table: "medication_statements");

            migrationBuilder.DropColumn(
                name: "sensitive",
                schema: "ehr",
                table: "conditions");

            migrationBuilder.DropColumn(
                name: "code_system",
                schema: "ehr",
                table: "allergy_intolerances");

            migrationBuilder.DropColumn(
                name: "severity",
                schema: "ehr",
                table: "allergy_intolerances");
        }
    }
}
