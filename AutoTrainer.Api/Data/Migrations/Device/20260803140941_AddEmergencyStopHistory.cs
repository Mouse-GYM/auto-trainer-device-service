using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoTrainer.Api.Data.Migrations.Device
{
    /// <inheritdoc />
    public partial class AddEmergencyStopHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmergencyStopHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventIndex = table.Column<long>(type: "INTEGER", nullable: false),
                    StopReasonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ResumeReasonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReasonText = table.Column<string>(type: "TEXT", nullable: false),
                    ActiveAlarms = table.Column<string>(type: "TEXT", nullable: true),
                    NotificationSentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyStopHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetectorHistory_CreatedAt",
                table: "DetectorHistory",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DetectorHistory_DetectorId_CreatedAt",
                table: "DetectorHistory",
                columns: new[] { "DetectorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AlarmHistory_AlarmId_CreatedAt",
                table: "AlarmHistory",
                columns: new[] { "AlarmId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AlarmHistory_CreatedAt",
                table: "AlarmHistory",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyStopHistory_OccurredAt",
                table: "EmergencyStopHistory",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmergencyStopHistory");

            migrationBuilder.DropIndex(
                name: "IX_DetectorHistory_CreatedAt",
                table: "DetectorHistory");

            migrationBuilder.DropIndex(
                name: "IX_DetectorHistory_DetectorId_CreatedAt",
                table: "DetectorHistory");

            migrationBuilder.DropIndex(
                name: "IX_AlarmHistory_AlarmId_CreatedAt",
                table: "AlarmHistory");

            migrationBuilder.DropIndex(
                name: "IX_AlarmHistory_CreatedAt",
                table: "AlarmHistory");
        }
    }
}
