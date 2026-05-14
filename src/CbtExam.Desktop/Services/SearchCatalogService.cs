using CbtExam.Desktop.Models;
using System.Collections.Generic;
using System.Linq;

namespace CbtExam.Desktop.Services;

public class SearchCatalogService
{
    private readonly ApiClient _api;

    public SearchCatalogService(ApiClient api)
    {
        _api = api;
    }

    public IEnumerable<PageRecord> GetPages() => new[]
    {
        new PageRecord { Id = "page-dash",     Title = "Dashboard",          Description = "Live exam control and session health",   Icon = "", Key = "Dashboard",   Category = "Pages" },
        new PageRecord { Id = "page-exam",     Title = "Exams",             Description = "Manage all examination records",         Icon = "", Key = "Exams",      Category = "Pages" },
        new PageRecord { Id = "page-questions",Title = "Questions",         Description = "Question bank management",               Icon = "", Key = "Questions",  Category = "Pages" },
        new PageRecord { Id = "page-students", Title = "Student Management",Description = "Manage student accounts and enrollment",  Icon = "", Key = "Students",   Category = "Pages" },
        new PageRecord { Id = "page-sessions", Title = "Sessions",         Description = "Create and monitor exam sessions",       Icon = "", Key = "Sessions",   Category = "Pages" },
        new PageRecord { Id = "page-results",  Title = "Results",          Description = "View student performance and reports",   Icon = "", Key = "Results",    Category = "Pages" },
        new PageRecord { Id = "page-reports",  Title = "Reports",          Description = "Generate and export examination reports", Icon = "", Key = "Reports",    Category = "Pages" },
        new PageRecord { Id = "page-monitor",  Title = "Monitor",          Description = "Real-time student activity monitoring",   Icon = "", Key = "Monitor",    Category = "Pages" },
        new PageRecord { Id = "page-devices",  Title = "Devices",          Description = "Connected student devices",              Icon = "", Key = "Devices",    Category = "Pages" },
        new PageRecord { Id = "page-notifs",   Title = "Notifications",    Description = "System notifications and alerts",        Icon = "", Key = "Notifications", Category = "Pages" },
        new PageRecord { Id = "page-settings", Title = "Settings",         Description = "Application configuration",              Icon = "", Key = "Settings",   Category = "Pages" },
    };

    public IEnumerable<SearchItemRecord> GetQuickActions() => new[]
    {
        new SearchItemRecord { Id = "action-new-session",  Title = "Start New Session",   Description = "Launch a new exam session",         Icon = "", Category = "Actions", Action = "Sessions" },
        new SearchItemRecord { Id = "action-new-exam",     Title = "Create New Exam",     Description = "Open exam creation form",           Icon = "", Category = "Actions", Action = "CreateExam" },
        new SearchItemRecord { Id = "action-reports",      Title = "View Student Reports", Description = "Jump to results and analytics",     Icon = "", Category = "Actions", Action = "Results" },
        new SearchItemRecord { Id = "action-question-bank",Title = "Manage Question Bank",Description = "Add / edit / import questions",      Icon = "", Category = "Actions", Action = "Questions" },
        new SearchItemRecord { Id = "action-students",     Title = "Add New Student",     Description = "Register a student account",        Icon = "", Category = "Actions", Action = "Students" },
        new SearchItemRecord { Id = "action-toggle-theme", Title = "Toggle Dark/Light",   Description = "Switch between dark and light theme",Icon = "", Category = "Actions", Action = "Theme" },
        new SearchItemRecord { Id = "action-server-toggle",Title = "Start / Stop Server",  Description = "Toggle the embedded server",         Icon = "", Category = "Actions", Action = "ServerToggle" },
        new SearchItemRecord { Id = "action-error-guide",  Title = "Error Guide",         Description = "Troubleshooting and help",           Icon = "", Category = "Actions", Action = "ErrorGuide" },
    };
}