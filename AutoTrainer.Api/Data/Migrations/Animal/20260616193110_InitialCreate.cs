using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoTrainer.Api.Data.Migrations.Animal
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnimalInfo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Identifier = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DcsSendX = table.Column<double>(type: "REAL", nullable: false),
                    DcsSendY = table.Column<double>(type: "REAL", nullable: false),
                    DcsSendZ = table.Column<double>(type: "REAL", nullable: false),
                    TargetYLimit = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimalInfo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PelletHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PelletHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PelletTotals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Stop = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Presented = table.Column<int>(type: "INTEGER", nullable: false),
                    Consumed = table.Column<int>(type: "INTEGER", nullable: false),
                    Other = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PelletTotals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReachEventHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    DelaySincePresented = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachEventHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReachEventTotals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Stop = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReachCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulReachCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EatenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DroppedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachEventTotals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enter = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Exit = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TrialCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionHistory", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnimalInfo");

            migrationBuilder.DropTable(
                name: "PelletHistory");

            migrationBuilder.DropTable(
                name: "PelletTotals");

            migrationBuilder.DropTable(
                name: "ReachEventHistory");

            migrationBuilder.DropTable(
                name: "ReachEventTotals");

            migrationBuilder.DropTable(
                name: "SessionHistory");
        }
    }
}
