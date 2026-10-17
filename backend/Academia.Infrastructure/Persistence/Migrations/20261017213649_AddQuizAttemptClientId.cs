using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizAttemptClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientAttemptId",
                table: "QuizAttempts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_ClientAttemptId",
                table: "QuizAttempts",
                columns: new[] { "UserId", "ClientAttemptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizAttempts_UserId_ClientAttemptId",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "ClientAttemptId",
                table: "QuizAttempts");
        }
    }
}
