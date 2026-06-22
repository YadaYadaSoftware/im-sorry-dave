using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SorryDave.JiraSync.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReporterAccountIdAndMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MentionedAccountIds",
                table: "WorkItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReporterAccountId",
                table: "WorkItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MentionedAccountIds",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ReporterAccountId",
                table: "WorkItems");
        }
    }
}
