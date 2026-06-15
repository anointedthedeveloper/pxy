using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CbtExam.Data.Migrations;

public partial class AddCustomSessionName : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>("CustomSessionName", "ExamSessions", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn("CustomSessionName", "ExamSessions");
    }
}
