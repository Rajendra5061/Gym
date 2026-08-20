using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpiryReminderEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpiryReminderEmails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    SentOnDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDateAtSend = table.Column<DateTime>(type: "date", nullable: false),
                    DaysLeftAtSend = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpiryReminderEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpiryReminderEmails_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpiryReminderEmails_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryReminderEmails_MemberId_SentOnDate",
                table: "ExpiryReminderEmails",
                columns: new[] { "MemberId", "SentOnDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryReminderEmails_SubscriptionId",
                table: "ExpiryReminderEmails",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpiryReminderEmails");
        }
    }
}
