using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailVoidApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionDaysToMailGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetentionDays",
                table: "MailGroup",
                type: "int",
                nullable: true,
                defaultValue: 3);
            
            // Update existing rows to have the default value
            migrationBuilder.Sql("UPDATE MailGroup SET RetentionDays = 3 WHERE RetentionDays IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetentionDays",
                table: "MailGroup");
        }
    }
}
