using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CbtExam.Data.Migrations;

public partial class AddQuestionBankSectionImage : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>("Section",  "QuestionBank", nullable: false, defaultValue: "");
        mb.AddColumn<string>("ImageUrl", "QuestionBank", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn("Section",  "QuestionBank");
        mb.DropColumn("ImageUrl", "QuestionBank");
    }
}
