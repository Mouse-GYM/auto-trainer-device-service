using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoTrainer.Api.Data.Migrations.Device
{
    /// <inheritdoc />
    public partial class DropAlarmHistoryDetectorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectorId",
                table: "AlarmHistory");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetectorId",
                table: "AlarmHistory",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
