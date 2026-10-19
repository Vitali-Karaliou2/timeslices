using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeSlices.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SelfCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    SliceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TrackId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExaminerId = table.Column<string>(type: "TEXT", nullable: true),
                    ExaminerComment = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attempts_AspNetUsers_ExaminerId",
                        column: x => x.ExaminerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Attempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Answers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuestionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    FollowUpText = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    SelfRating = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ShownAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActiveMs = table.Column<int>(type: "INTEGER", nullable: false),
                    PasteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FollowUpActiveMs = table.Column<int>(type: "INTEGER", nullable: true),
                    FollowUpPasteCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Answers_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Answers_AttemptId_QuestionId",
                table: "Answers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ExaminerId",
                table: "Attempts",
                column: "ExaminerId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_Open",
                table: "Attempts",
                columns: new[] { "UserId", "SliceId", "TrackId" },
                unique: true,
                filter: "\"Status\" IN ('InProgress', 'Submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_Status_SubmittedAt",
                table: "Attempts",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_SliceId_TrackId",
                table: "Attempts",
                columns: new[] { "UserId", "SliceId", "TrackId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Answers");

            migrationBuilder.DropTable(
                name: "Attempts");
        }
    }
}
