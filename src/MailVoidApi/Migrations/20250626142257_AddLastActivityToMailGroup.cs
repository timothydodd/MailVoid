using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailVoidApi.Migrations
{
    /// <inheritdoc />
    public partial class AddLastActivityToMailGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {



            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivity",
                table: "MailGroup",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.DropColumn(
                name: "LastActivity",
                table: "MailGroup");



        }
    }
}
