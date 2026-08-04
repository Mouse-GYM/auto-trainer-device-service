using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoTrainer.Api.Data.Migrations.Device
{
    /// <summary>
    /// Rewrites already-stored reason codes from the service's original numbering to the producer's.
    ///
    /// StopReasonId/ResumeReasonId used to hold Models.Emergency*Reason, authored before the Python API
    /// formalized these enums. The producer then published ApiEmergency*Reason with DIFFERENT values, and
    /// those are now canonical, so existing rows would otherwise be silently mislabelled -- a stored 201
    /// meant UserButton and now means DiamondCoordCheck.
    ///
    /// The column type does not change (both are ints), so EF scaffolds nothing; this is data only.
    ///
    /// Each statement is a single UPDATE with a CASE, which SQLite evaluates against each row's
    /// pre-update value. Sequential UPDATEs would cascade (201 -> 301, then that same row 301 -> 401).
    /// </summary>
    public partial class RemapEmergencyReasonCodesToApiValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stop: UserButton 201->301, RpcService 301->401, DiamondCoordCheck 401->201.
            // Unknown (0) and AlarmMonitor (101) already agree.
            migrationBuilder.Sql(@"
                UPDATE ""EmergencyStopHistory""
                SET ""StopReasonId"" = CASE ""StopReasonId""
                    WHEN 201 THEN 301
                    WHEN 301 THEN 401
                    WHEN 401 THEN 201
                    ELSE ""StopReasonId"" END
                WHERE ""StopReasonId"" IS NOT NULL;");

            // Resume: NoValidConditionRemaining 102->201 (AlarmMonitorStatusChange), UserButton 201->301,
            // RpcService 301->401. Unknown (0) and the alarm-monitor resume (101) already agree.
            migrationBuilder.Sql(@"
                UPDATE ""EmergencyStopHistory""
                SET ""ResumeReasonId"" = CASE ""ResumeReasonId""
                    WHEN 102 THEN 201
                    WHEN 201 THEN 301
                    WHEN 301 THEN 401
                    ELSE ""ResumeReasonId"" END
                WHERE ""ResumeReasonId"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""EmergencyStopHistory""
                SET ""StopReasonId"" = CASE ""StopReasonId""
                    WHEN 301 THEN 201
                    WHEN 401 THEN 301
                    WHEN 201 THEN 401
                    ELSE ""StopReasonId"" END
                WHERE ""StopReasonId"" IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE ""EmergencyStopHistory""
                SET ""ResumeReasonId"" = CASE ""ResumeReasonId""
                    WHEN 201 THEN 102
                    WHEN 301 THEN 201
                    WHEN 401 THEN 301
                    ELSE ""ResumeReasonId"" END
                WHERE ""ResumeReasonId"" IS NOT NULL;");
        }
    }
}
