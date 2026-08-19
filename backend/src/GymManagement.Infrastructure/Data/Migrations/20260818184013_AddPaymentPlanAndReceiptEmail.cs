using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentPlanAndReceiptEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MembershipPlanId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiptEmailedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MembershipPlanId",
                table: "Payments",
                column: "MembershipPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_MembershipPlans_MembershipPlanId",
                table: "Payments",
                column: "MembershipPlanId",
                principalTable: "MembershipPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_MembershipPlans_MembershipPlanId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_MembershipPlanId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "MembershipPlanId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReceiptEmailedAtUtc",
                table: "Payments");
        }
    }
}
