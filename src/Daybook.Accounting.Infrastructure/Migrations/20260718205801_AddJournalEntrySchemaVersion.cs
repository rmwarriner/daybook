using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daybook.Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalEntrySchemaVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "JournalEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "JournalEntries");
        }
    }
}