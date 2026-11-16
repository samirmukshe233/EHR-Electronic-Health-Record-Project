using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ehr.Migrations
{
    /// <inheritdoc />
    public partial class Observations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_orders_id_patient_id",
                schema: "ehr",
                table: "orders",
                columns: new[] { "id", "patient_id" });

            migrationBuilder.CreateTable(
                name: "observations",
                schema: "ehr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    display = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reference_low = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    reference_high = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => x.id);
                    table.CheckConstraint("ck_observations_reference_range", "reference_low <= reference_high");
                    table.CheckConstraint("ck_observations_status", "status IN ('registered', 'preliminary', 'final', 'amended', 'corrected', 'cancelled', 'entered-in-error')");
                    table.ForeignKey(
                        name: "fk_observations_orders_order_id_patient_id",
                        columns: x => new { x.order_id, x.patient_id },
                        principalSchema: "ehr",
                        principalTable: "orders",
                        principalColumns: new[] { "id", "patient_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "ehr",
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_observations_order_id_patient_id",
                schema: "ehr",
                table: "observations",
                columns: new[] { "order_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_observations_patient_id_effective_at",
                schema: "ehr",
                table: "observations",
                columns: new[] { "patient_id", "effective_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observations",
                schema: "ehr");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_orders_id_patient_id",
                schema: "ehr",
                table: "orders");
        }
    }
}
