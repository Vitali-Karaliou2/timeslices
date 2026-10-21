using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeSlices.Web.Data.Migrations
{
    /// <summary>
    /// Прохождение по срезу, а не по тематическому срезу (ADR 0014, п. В): TrackId убирается
    /// из UserSliceStates и Attempts. Строки состояния сливаются по (пользователь, срез): старшее
    /// состояние, IsRepeat — логическое ИЛИ. Попытки не удаляются; если по срезу оказалось несколько
    /// незавершённых (из разных треков), открытой остаётся отправленная экзаменатору (Submitted), а если таких нет
    /// или их несколько — последняя; остальные закрываются как
    /// возвращённые с пояснением — иначе не создать уникальный индекс «одна открытая попытка на срез».
    /// </summary>
    public partial class ProgressBySlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSliceStates_UserId_SliceId_TrackId",
                table: "UserSliceStates");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_Open",
                table: "Attempts");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_UserId_SliceId_TrackId",
                table: "Attempts");

            migrationBuilder.Sql(MergeStatesSql);
            migrationBuilder.Sql(CloseDuplicateOpenAttemptsSql);

            migrationBuilder.DropColumn(
                name: "TrackId",
                table: "UserSliceStates");

            migrationBuilder.DropColumn(
                name: "TrackId",
                table: "Attempts");

            migrationBuilder.CreateIndex(
                name: "IX_UserSliceStates_UserId_SliceId",
                table: "UserSliceStates",
                columns: new[] { "UserId", "SliceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_Open",
                table: "Attempts",
                columns: new[] { "UserId", "SliceId" },
                unique: true,
                filter: "\"Status\" IN ('InProgress', 'Submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_SliceId",
                table: "Attempts",
                columns: new[] { "UserId", "SliceId" });
        }

        /// <summary>Ранг состояния: Done старше InProgress (хранятся только они; остальные — на всякий случай).</summary>
        private const string Rank =
            "CASE {0}.\"State\" WHEN 'Done' THEN 3 WHEN 'InProgress' THEN 2 WHEN 'Available' THEN 1 ELSE 0 END";

        // IsRepeat — логическое ИЛИ (план сессии 08, п. 2), но только если итог — InProgress: «повторно» имеет
        // смысл лишь у начатого среза, а Done с признаком повтора оболочка нигде не создаёт.
        internal static readonly string MergeStatesSql = $"""
            UPDATE "UserSliceStates" AS s SET
                "State" = (SELECT t."State" FROM "UserSliceStates" AS t
                           WHERE t."UserId" = s."UserId" AND t."SliceId" = s."SliceId"
                           ORDER BY {string.Format(Rank, "t")} DESC LIMIT 1),
                "IsRepeat" = (SELECT CASE WHEN MAX({string.Format(Rank, "t")}) = 3 THEN 0 ELSE MAX(t."IsRepeat") END
                              FROM "UserSliceStates" AS t
                              WHERE t."UserId" = s."UserId" AND t."SliceId" = s."SliceId"),
                "UpdatedAt" = (SELECT MAX(t."UpdatedAt") FROM "UserSliceStates" AS t
                               WHERE t."UserId" = s."UserId" AND t."SliceId" = s."SliceId");
            DELETE FROM "UserSliceStates"
            WHERE "Id" NOT IN (SELECT MIN("Id") FROM "UserSliceStates" GROUP BY "UserId", "SliceId");
            """;

        internal static readonly string CloseDuplicateOpenAttemptsSql = """
            UPDATE "Attempts" SET
                "Status" = 'Returned',
                "DecidedAt" = strftime('%Y-%m-%d %H:%M:%S', 'now'),
                "ExaminerComment" = 'Закрыта при переходе на учёт по срезу (ADR 0014): по этому срезу была более поздняя незавершённая попытка из другого трека.'
            WHERE "Status" IN ('InProgress', 'Submitted')
              AND EXISTS (SELECT 1 FROM "Attempts" AS b
                          WHERE b."UserId" = "Attempts"."UserId" AND b."SliceId" = "Attempts"."SliceId"
                            AND b."Status" IN ('InProgress', 'Submitted')
                            AND ((b."Status" = 'Submitted' AND "Attempts"."Status" = 'InProgress')
                                 OR (b."Status" = "Attempts"."Status" AND b."Id" > "Attempts"."Id")));
            """;

        /// <inheritdoc />
        /// <remarks>Обратно трек не восстановить: TrackId вернётся пустым, слитые строки останутся слитыми.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSliceStates_UserId_SliceId",
                table: "UserSliceStates");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_Open",
                table: "Attempts");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_UserId_SliceId",
                table: "Attempts");

            migrationBuilder.AddColumn<string>(
                name: "TrackId",
                table: "UserSliceStates",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrackId",
                table: "Attempts",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_UserSliceStates_UserId_SliceId_TrackId",
                table: "UserSliceStates",
                columns: new[] { "UserId", "SliceId", "TrackId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_Open",
                table: "Attempts",
                columns: new[] { "UserId", "SliceId", "TrackId" },
                unique: true,
                filter: "\"Status\" IN ('InProgress', 'Submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_SliceId_TrackId",
                table: "Attempts",
                columns: new[] { "UserId", "SliceId", "TrackId" });
        }
    }
}
