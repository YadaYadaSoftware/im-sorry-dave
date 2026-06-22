using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SorryDave.JiraSync.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSummarization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CapturedMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    Ts = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ThreadTs = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorId = table.Column<string>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PostedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CapturedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapturedMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostCursors",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    LastPostedTs = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostCursors", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "SummaryCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: true),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    RecordIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    WindowFromTs = table.Column<string>(type: "TEXT", nullable: true),
                    WindowToTs = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummaryCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapturedMessages_ChannelId_Ts",
                table: "CapturedMessages",
                columns: new[] { "ChannelId", "Ts" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummaryCandidates_WorkItemKey_Status",
                table: "SummaryCandidates",
                columns: new[] { "WorkItemKey", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapturedMessages");

            migrationBuilder.DropTable(
                name: "PostCursors");

            migrationBuilder.DropTable(
                name: "SummaryCandidates");
        }
    }
}
