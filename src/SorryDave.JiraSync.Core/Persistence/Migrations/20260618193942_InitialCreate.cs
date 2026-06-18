using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SorryDave.JiraSync.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkItems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProjectKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IssueType = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AssigneeAccountId = table.Column<string>(type: "TEXT", nullable: true),
                    AssigneeDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    ReporterDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Labels = table.Column<string>(type: "TEXT", nullable: false),
                    JiraUpdated = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSyncedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "WriteBackRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    RecordIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    JiraCommentId = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NextAttemptUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WriteBackRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceMappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WorkItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceMappings_WorkItems_WorkItemKey",
                        column: x => x.WorkItemKey,
                        principalTable: "WorkItems",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMappings_ResourceType_ResourceId",
                table: "ResourceMappings",
                columns: new[] { "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMappings_WorkItemKey",
                table: "ResourceMappings",
                column: "WorkItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_JiraUpdated",
                table: "WorkItems",
                column: "JiraUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ProjectKey",
                table: "WorkItems",
                column: "ProjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_WriteBackRecords_Status_NextAttemptUtc",
                table: "WriteBackRecords",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WriteBackRecords_WorkItemKey_RecordIdentity",
                table: "WriteBackRecords",
                columns: new[] { "WorkItemKey", "RecordIdentity" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceMappings");

            migrationBuilder.DropTable(
                name: "WriteBackRecords");

            migrationBuilder.DropTable(
                name: "WorkItems");
        }
    }
}
