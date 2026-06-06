using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CbtExam.Data.Migrations
{
    public partial class AddSourceIdToQuestionBank : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "QuestionBank",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionBank_SourceId",
                table: "QuestionBank",
                column: "SourceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_QuestionBank_SourceId", table: "QuestionBank");
            migrationBuilder.DropColumn(name: "SourceId", table: "QuestionBank");
        }
    }
}
