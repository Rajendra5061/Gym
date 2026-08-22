using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemberNotificationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberNotificationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    SentOnDate = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmailSent = table.Column<bool>(type: "bit", nullable: false),
                    WhatsAppSent = table.Column<bool>(type: "bit", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberNotificationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberNotificationLogs_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberNotificationLogs_MemberId_Kind_DeduplicationKey",
                table: "MemberNotificationLogs",
                columns: new[] { "MemberId", "Kind", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberNotificationLogs_SentOnDate",
                table: "MemberNotificationLogs",
                column: "SentOnDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberNotificationLogs");
        }
    }
}
