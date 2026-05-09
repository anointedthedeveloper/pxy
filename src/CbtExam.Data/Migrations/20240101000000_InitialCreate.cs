using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CbtExam.Data.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable("Exams", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            Title = t.Column<string>(nullable: false),
            Subject = t.Column<string>(nullable: false),
            DurationMinutes = t.Column<int>(nullable: false),
            ShuffleQuestions = t.Column<bool>(nullable: false, defaultValue: true),
            ShuffleOptions = t.Column<bool>(nullable: false, defaultValue: true),
            CreatedAt = t.Column<DateTime>(nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Exams", x => x.Id));

        mb.CreateTable("Students", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            FullName = t.Column<string>(nullable: false),
            StudentId = t.Column<string>(nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Students", x => x.Id));

        mb.CreateTable("Questions", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            ExamId = t.Column<int>(nullable: false),
            QuestionNumber = t.Column<int>(nullable: false),
            Text = t.Column<string>(nullable: false),
            OptionsJson = t.Column<string>(nullable: false),
            CorrectAnswer = t.Column<string>(nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_Questions", x => x.Id);
            t.ForeignKey("FK_Questions_Exams_ExamId", x => x.ExamId, "Exams", "Id", onDelete: ReferentialAction.Cascade);
        });

        mb.CreateTable("ExamSessions", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            ExamId = t.Column<int>(nullable: false),
            SessionCode = t.Column<string>(nullable: false),
            StartedAt = t.Column<DateTime>(nullable: false),
            EndedAt = t.Column<DateTime>(nullable: true),
            IsActive = t.Column<bool>(nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ExamSessions", x => x.Id);
            t.ForeignKey("FK_ExamSessions_Exams_ExamId", x => x.ExamId, "Exams", "Id", onDelete: ReferentialAction.Cascade);
        });

        mb.CreateTable("StudentExams", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            StudentId = t.Column<int>(nullable: false),
            SessionId = t.Column<int>(nullable: false),
            JoinedAt = t.Column<DateTime>(nullable: false),
            SubmittedAt = t.Column<DateTime>(nullable: true),
            IsSubmitted = t.Column<bool>(nullable: false),
            Score = t.Column<int>(nullable: false),
            TabSwitchCount = t.Column<int>(nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_StudentExams", x => x.Id);
            t.ForeignKey("FK_StudentExams_Students_StudentId", x => x.StudentId, "Students", "Id", onDelete: ReferentialAction.Cascade);
            t.ForeignKey("FK_StudentExams_ExamSessions_SessionId", x => x.SessionId, "ExamSessions", "Id", onDelete: ReferentialAction.Cascade);
        });

        mb.CreateTable("Answers", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            StudentExamId = t.Column<int>(nullable: false),
            QuestionId = t.Column<int>(nullable: false),
            SelectedAnswer = t.Column<string>(nullable: false),
            IsCorrect = t.Column<bool>(nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_Answers", x => x.Id);
            t.ForeignKey("FK_Answers_StudentExams_StudentExamId", x => x.StudentExamId, "StudentExams", "Id", onDelete: ReferentialAction.Cascade);
            t.ForeignKey("FK_Answers_Questions_QuestionId", x => x.QuestionId, "Questions", "Id", onDelete: ReferentialAction.Cascade);
        });

        mb.CreateIndex("IX_ExamSessions_SessionCode", "ExamSessions", "SessionCode", unique: true);
        mb.CreateIndex("IX_Questions_ExamId", "Questions", "ExamId");
        mb.CreateIndex("IX_StudentExams_SessionId", "StudentExams", "SessionId");
        mb.CreateIndex("IX_StudentExams_StudentId", "StudentExams", "StudentId");
        mb.CreateIndex("IX_Answers_StudentExamId", "Answers", "StudentExamId");
        mb.CreateIndex("IX_Answers_QuestionId", "Answers", "QuestionId");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("Answers");
        mb.DropTable("StudentExams");
        mb.DropTable("ExamSessions");
        mb.DropTable("Questions");
        mb.DropTable("Students");
        mb.DropTable("Exams");
    }
}
