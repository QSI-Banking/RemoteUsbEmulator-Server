using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class OtpSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OtpSecrets",
                schema: "usbipd",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Secret = table.Column<byte[]>(type: "varbinary(64)", maxLength: 64, nullable: false),
                    LastAcceptedStep = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpSecrets", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_OtpSecrets_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "usbipd",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtpSecrets",
                schema: "usbipd");
        }
    }
}
