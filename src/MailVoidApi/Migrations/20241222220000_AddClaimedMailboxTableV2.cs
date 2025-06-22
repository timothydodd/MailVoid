using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MailVoidApi.Data;

#nullable disable

namespace MailVoidApi.Migrations
{
    [DbContext(typeof(MailVoidDbContext))]
    [Migration("20241222220000_AddClaimedMailboxTableV2")]
    /// <inheritdoc />
    public partial class AddClaimedMailboxTableV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimedMailbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EmailAddress = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClaimedOn = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimedMailbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimedMailbox_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"
                CREATE INDEX `IX_ClaimedMailbox_EmailAddress` 
                ON `ClaimedMailbox` (`EmailAddress`(255));
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX `IX_ClaimedMailbox_EmailAddress_UserId` 
                ON `ClaimedMailbox` (`EmailAddress`(255), `UserId`);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimedMailbox_UserId",
                table: "ClaimedMailbox",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_ClaimedMailbox_EmailAddress` ON `ClaimedMailbox`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_ClaimedMailbox_EmailAddress_UserId` ON `ClaimedMailbox`;");
            
            migrationBuilder.DropTable(
                name: "ClaimedMailbox");
        }
    }
}