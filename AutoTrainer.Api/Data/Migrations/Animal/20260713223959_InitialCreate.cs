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
                name: "AnimalHistory",
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
                    table.PrimaryKey("PK_AnimalHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReachStatusDay",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PelletsPresented = table.Column<int>(type: "INTEGER", nullable: false),
                    PelletsConsumed = table.Column<int>(type: "INTEGER", nullable: false),
                    Reaches = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulReaches = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachStatusDay", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReachStatusTotal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PelletsPresented = table.Column<int>(type: "INTEGER", nullable: false),
                    PelletsConsumed = table.Column<int>(type: "INTEGER", nullable: false),
                    Reaches = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulReaches = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachStatusTotal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Session",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Identifier = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsAnalysisDeferred = table.Column<bool>(type: "INTEGER", nullable: true),
                    CaptureTrialCount = table.Column<int>(type: "INTEGER", nullable: true),
                    AnalysisTrialCount = table.Column<int>(type: "INTEGER", nullable: true),
                    FailedTrialCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Session", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BatchAnalysis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Identifier = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnalysisTrialCount = table.Column<int>(type: "INTEGER", nullable: true),
                    FailedTrialCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchAnalysis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchAnalysis_Session_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Session",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Identifier = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchAnalysisId = table.Column<int>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PelletPresentedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PelletSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnimalSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RightHandSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CaptureEndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialSegmentationBeginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialSegmentationEndAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialSegmentationError = table.Column<string>(type: "TEXT", nullable: true),
                    IntertrialSegmentationSaveAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialSegmentationSaveLocation = table.Column<string>(type: "TEXT", nullable: true),
                    IntertrialSegmentationSaveError = table.Column<string>(type: "TEXT", nullable: true),
                    IntertrialDetectionBeginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialDetectionEndAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialDetectionError = table.Column<string>(type: "TEXT", nullable: true),
                    IntertrialDetectionSaveAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntertrialDetectionSaveLocation = table.Column<string>(type: "TEXT", nullable: true),
                    IntertrialDetectionSaveError = table.Column<string>(type: "TEXT", nullable: true),
                    IntertrialPelletShift = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trial_BatchAnalysis_BatchAnalysisId",
                        column: x => x.BatchAnalysisId,
                        principalTable: "BatchAnalysis",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Trial_Session_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Session",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReachEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrialId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_ReachEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReachEvent_Trial_TrialId",
                        column: x => x.TrialId,
                        principalTable: "Trial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchAnalysis_Identifier",
                table: "BatchAnalysis",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatchAnalysis_SessionId",
                table: "BatchAnalysis",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReachEvent_TrialId",
                table: "ReachEvent",
                column: "TrialId");

            migrationBuilder.CreateIndex(
                name: "IX_Session_Identifier",
                table: "Session",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trial_BatchAnalysisId",
                table: "Trial",
                column: "BatchAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_Trial_SessionId_Identifier",
                table: "Trial",
                columns: new[] { "SessionId", "Identifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnimalHistory");

            migrationBuilder.DropTable(
                name: "ReachEvent");

            migrationBuilder.DropTable(
                name: "ReachStatusDay");

            migrationBuilder.DropTable(
                name: "ReachStatusTotal");

            migrationBuilder.DropTable(
                name: "Trial");

            migrationBuilder.DropTable(
                name: "BatchAnalysis");

            migrationBuilder.DropTable(
                name: "Session");
        }
    }
}
