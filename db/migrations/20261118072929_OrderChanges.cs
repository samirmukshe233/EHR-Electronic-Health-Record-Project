using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ehr.Migrations
{
    /// <inheritdoc />
    public partial class OrderChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discontinue_reason",
                schema: "ehr",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discontinued_at",
                schema: "ehr",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "discontinuer_id",
                schema: "ehr",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_order_id",
                schema: "ehr",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaces_order_id",
                schema: "ehr",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_discontinuer_id",
                schema: "ehr",
                table: "orders",
                column: "discontinuer_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_replaced_by_order_id",
                schema: "ehr",
                table: "orders",
                column: "replaced_by_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_replaces_order_id",
                schema: "ehr",
                table: "orders",
                column: "replaces_order_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_discontinued",
                schema: "ehr",
                table: "orders",
                sql: "(status = 'revoked') = (discontinued_at IS NOT NULL) AND (discontinued_at IS NULL) = (discontinuer_id IS NULL) AND (discontinued_at IS NULL) = (discontinue_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_replaced",
                schema: "ehr",
                table: "orders",
                sql: "replaced_by_order_id IS NULL OR status = 'revoked'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_replaces_another",
                schema: "ehr",
                table: "orders",
                sql: "replaces_order_id <> id AND replaced_by_order_id <> id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_orders_replaced_by_order_id",
                schema: "ehr",
                table: "orders",
                column: "replaced_by_order_id",
                principalSchema: "ehr",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_orders_replaces_order_id",
                schema: "ehr",
                table: "orders",
                column: "replaces_order_id",
                principalSchema: "ehr",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_staff_users_discontinuer_id",
                schema: "ehr",
                table: "orders",
                column: "discontinuer_id",
                principalSchema: "ehr",
                principalTable: "staff_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_orders_orders_replaced_by_order_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_orders_replaces_order_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_staff_users_discontinuer_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_discontinuer_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_replaced_by_order_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_replaces_order_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_discontinued",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_replaced",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_replaces_another",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discontinue_reason",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discontinued_at",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "discontinuer_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "replaced_by_order_id",
                schema: "ehr",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "replaces_order_id",
                schema: "ehr",
                table: "orders");
        }
    }
}
