using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentRequestGatewayOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GatewayProvider",
                table: "PaymentRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MembershipPlanId",
                table: "PaymentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderId",
                table: "PaymentRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAtUtc",
                table: "PaymentRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentId",
                table: "PaymentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentUrl",
                table: "PaymentRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrData",
                table: "PaymentRequests",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PaymentRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_MembershipPlanId",
                table: "PaymentRequests",
                column: "MembershipPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_PaymentId",
                table: "PaymentRequests",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Status",
                table: "PaymentRequests",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRequests_MembershipPlans_MembershipPlanId",
                table: "PaymentRequests",
                column: "MembershipPlanId",
                principalTable: "MembershipPlans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRequests_Payments_PaymentId",
                table: "PaymentRequests",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRequests_MembershipPlans_MembershipPlanId",
                table: "PaymentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRequests_Payments_PaymentId",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_MembershipPlanId",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_PaymentId",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_Status",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "GatewayProvider",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "MembershipPlanId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "PaidAtUtc",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "PaymentUrl",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "QrData",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PaymentRequests");
        }
    }
}
