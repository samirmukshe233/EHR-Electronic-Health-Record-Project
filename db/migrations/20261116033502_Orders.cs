using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ehr.Migrations
{
    /// <inheritdoc />
    public partial class Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_catalog_items",
                schema: "ehr",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_catalog_items", x => x.code);
                    table.UniqueConstraint("ak_order_catalog_items_type_code", x => new { x.type, x.code });
                    table.CheckConstraint("ck_order_catalog_items_type", "type IN ('lab', 'imaging', 'medication', 'referral')");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "ehr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    catalog_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    signer_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "NULLIF(current_setting('app.user_id', true), '')::uuid"),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    duplicate_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_details", "jsonb_typeof(details) = 'object'");
                    table.CheckConstraint("ck_orders_status", "status IN ('draft', 'active', 'on-hold', 'revoked', 'completed', 'entered-in-error', 'unknown')");
                    table.CheckConstraint("ck_orders_type", "type IN ('lab', 'imaging', 'medication', 'referral')");
                    table.ForeignKey(
                        name: "fk_orders_order_catalog_items_type_catalog_code",
                        columns: x => new { x.type, x.catalog_code },
                        principalSchema: "ehr",
                        principalTable: "order_catalog_items",
                        principalColumns: new[] { "type", "code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "ehr",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_staff_users_signer_id",
                        column: x => x.signer_id,
                        principalSchema: "ehr",
                        principalTable: "staff_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "ehr",
                table: "order_catalog_items",
                columns: new[] { "code", "display", "type" },
                values: new object[,]
                {
                    { "SYN-IMG-001", "Chest X-ray, 2 views", "imaging" },
                    { "SYN-IMG-002", "Lumbar spine X-ray", "imaging" },
                    { "SYN-IMG-003", "Abdominal ultrasound", "imaging" },
                    { "SYN-IMG-004", "CT head without contrast", "imaging" },
                    { "SYN-IMG-005", "MRI lumbar spine without contrast", "imaging" },
                    { "SYN-IMG-006", "Screening mammogram", "imaging" },
                    { "SYN-LAB-001", "Complete blood count (CBC)", "lab" },
                    { "SYN-LAB-002", "Basic metabolic panel (BMP)", "lab" },
                    { "SYN-LAB-003", "Comprehensive metabolic panel (CMP)", "lab" },
                    { "SYN-LAB-004", "Lipid panel", "lab" },
                    { "SYN-LAB-005", "Hemoglobin A1c", "lab" },
                    { "SYN-LAB-006", "Thyroid-stimulating hormone (TSH)", "lab" },
                    { "SYN-LAB-007", "Urinalysis", "lab" },
                    { "SYN-LAB-008", "Vitamin D, 25-hydroxy", "lab" },
                    { "SYN-MED-001", "Lisinopril 10 mg tablet", "medication" },
                    { "SYN-MED-002", "Metformin 500 mg tablet", "medication" },
                    { "SYN-MED-003", "Atorvastatin 20 mg tablet", "medication" },
                    { "SYN-MED-004", "Amlodipine 5 mg tablet", "medication" },
                    { "SYN-MED-005", "Amoxicillin 500 mg capsule", "medication" },
                    { "SYN-MED-006", "Albuterol 90 mcg inhaler", "medication" },
                    { "SYN-MED-007", "Sertraline 50 mg tablet", "medication" },
                    { "SYN-MED-008", "Ibuprofen 400 mg tablet", "medication" },
                    { "SYN-REF-001", "Cardiology referral", "referral" },
                    { "SYN-REF-002", "Dermatology referral", "referral" },
                    { "SYN-REF-003", "Endocrinology referral", "referral" },
                    { "SYN-REF-004", "Gastroenterology referral", "referral" },
                    { "SYN-REF-005", "Orthopedics referral", "referral" },
                    { "SYN-REF-006", "Physical therapy referral", "referral" },
                    { "SYN-REF-007", "Psychiatry referral", "referral" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_catalog_items_type_display",
                schema: "ehr",
                table: "order_catalog_items",
                columns: new[] { "type", "display" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_patient_id_signed_at",
                schema: "ehr",
                table: "orders",
                columns: new[] { "patient_id", "signed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_signer_id",
                schema: "ehr",
                table: "orders",
                column: "signer_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_type_catalog_code",
                schema: "ehr",
                table: "orders",
                columns: new[] { "type", "catalog_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders",
                schema: "ehr");

            migrationBuilder.DropTable(
                name: "order_catalog_items",
                schema: "ehr");
        }
    }
}
