using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoTrainer.Api.Data.Migrations.Animal
{
    /// <inheritdoc />
    public partial class AddIntertrialResultTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ReachEvent is dropped, not renamed to RawReachEvent and backfilled: every existing row predates
            // IntertrialResult and has no result to belong to, so the NOT NULL IntertrialResultId on the new
            // table would leave it pointing at a result id that does not exist. Dropping also clears the
            // soft-deleted rows, which is intended -- there is nothing to preserve them for.
            migrationBuilder.DropTable(
                name: "ReachEvent");

            migrationBuilder.CreateTable(
                name: "IntertrialResult",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrialId = table.Column<int>(type: "INTEGER", nullable: false),
                    RhMaxVpList = table.Column<string>(type: "TEXT", nullable: true),
                    FoodConsumed = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulReaches = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalReaches = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntertrialResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntertrialResult_Trial_TrialId",
                        column: x => x.TrialId,
                        principalTable: "Trial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HandReachEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    DelaySincePresented = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandReachEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandReachEvent_IntertrialResult_IntertrialResultId",
                        column: x => x.IntertrialResultId,
                        principalTable: "IntertrialResult",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OtherReachEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    DelaySincePresented = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtherReachEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtherReachEvent_IntertrialResult_IntertrialResultId",
                        column: x => x.IntertrialResultId,
                        principalTable: "IntertrialResult",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RawReachEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrialId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    DelaySincePresented = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawReachEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawReachEvent_IntertrialResult_IntertrialResultId",
                        column: x => x.IntertrialResultId,
                        principalTable: "IntertrialResult",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawReachEvent_Trial_TrialId",
                        column: x => x.TrialId,
                        principalTable: "Trial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandReachEvent_IntertrialResultId",
                table: "HandReachEvent",
                column: "IntertrialResultId");

            migrationBuilder.CreateIndex(
                name: "IX_IntertrialResult_TrialId",
                table: "IntertrialResult",
                column: "TrialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtherReachEvent_IntertrialResultId",
                table: "OtherReachEvent",
                column: "IntertrialResultId");

            migrationBuilder.CreateIndex(
                name: "IX_RawReachEvent_IntertrialResultId",
                table: "RawReachEvent",
                column: "IntertrialResultId");

            migrationBuilder.CreateIndex(
                name: "IX_RawReachEvent_TrialId",
                table: "RawReachEvent",
                column: "TrialId");
        }

        /// <inheritdoc />
        // Reverses the schema only: the ReachEvent rows Up() dropped are not recoverable by it.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandReachEvent");

            migrationBuilder.DropTable(
                name: "OtherReachEvent");

            migrationBuilder.DropTable(
                name: "RawReachEvent");

            migrationBuilder.DropTable(
                name: "IntertrialResult");

            migrationBuilder.CreateTable(
                name: "ReachEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrialId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DelaySincePresented = table.Column<double>(type: "REAL", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxFrame = table.Column<int>(type: "INTEGER", nullable: true),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReachEvent_Trial_TrialId",
                        column: x => x.TrialId,
                        principalTable: "Trial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReachEvent_TrialId",
                table: "ReachEvent",
                column: "TrialId");
        }
    }
}
