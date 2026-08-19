using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGatewayEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentGatewayEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    GatewayStatus = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    GatewayTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    PayloadDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentGatewayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentGatewayEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayEvents_PaymentId",
                table: "PaymentGatewayEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayEvents_PaymentReference",
                table: "PaymentGatewayEvents",
                column: "PaymentReference");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayEvents_Provider_EventId",
                table: "PaymentGatewayEvents",
                columns: new[] { "Provider", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayEvents_ReceivedAtUtc",
                table: "PaymentGatewayEvents",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentGatewayEvents");
        }
    }
}
