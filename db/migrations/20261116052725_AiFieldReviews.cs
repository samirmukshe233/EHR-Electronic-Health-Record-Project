using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ehr.Migrations
{
    /// <inheritdoc />
    public partial class AiFieldReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_note_reviews",
                schema: "ehr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    max_seconds = table.Column<int>(type: "integer", nullable: false),
                    max_chunk_bytes = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_version = table.Column<long>(type: "bigint", nullable: false),
                    imported_version = table.Column<long>(type: "bigint", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_note_reviews", x => x.id);
                    table.UniqueConstraint("ak_ai_note_reviews_id_patient_id", x => new { x.id, x.patient_id });
                    table.ForeignKey(
                        name: "fk_ai_note_reviews_clinical_drafts_draft_id",
                        column: x => x.draft_id,
                        principalSchema: "ehr",
                        principalTable: "clinical_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_note_reviews_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "ehr",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_note_reviews_staff_users_clinician_id",
                        column: x => x.clinician_id,
                        principalSchema: "ehr",
                        principalTable: "staff_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_field_proposals",
                schema: "ehr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    original_value = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    approved_value = table.Column<string>(type: "text", nullable: true),
                    decision_key = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_field_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_field_proposals_ai_note_reviews_review_id_patient_id",
                        columns: x => new { x.review_id, x.patient_id },
                        principalSchema: "ehr",
                        principalTable: "ai_note_reviews",
                        principalColumns: new[] { "id", "patient_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_field_proposals_staff_users_reviewer_id",
                        column: x => x.reviewer_id,
                        principalSchema: "ehr",
                        principalTable: "staff_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_field_proposals_decision_key",
                schema: "ehr",
                table: "ai_field_proposals",
                column: "decision_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_field_proposals_review_id_field",
                schema: "ehr",
                table: "ai_field_proposals",
                columns: new[] { "review_id", "field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_field_proposals_review_id_patient_id",
                schema: "ehr",
                table: "ai_field_proposals",
                columns: new[] { "review_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_field_proposals_reviewer_id",
                schema: "ehr",
                table: "ai_field_proposals",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_note_reviews_clinician_id",
                schema: "ehr",
                table: "ai_note_reviews",
                column: "clinician_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_note_reviews_draft_id_state",
                schema: "ehr",
                table: "ai_note_reviews",
                columns: new[] { "draft_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_note_reviews_patient_id",
                schema: "ehr",
                table: "ai_note_reviews",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_note_reviews_session_id",
                schema: "ehr",
                table: "ai_note_reviews",
                column: "session_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_field_proposals",
                schema: "ehr");

            migrationBuilder.DropTable(
                name: "ai_note_reviews",
                schema: "ehr");
        }
    }
}
