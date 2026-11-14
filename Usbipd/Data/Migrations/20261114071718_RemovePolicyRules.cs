using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePolicyRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyRules",
                schema: "usbipd");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyRules",
                schema: "usbipd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HardwareId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyRules_ServerName",
                schema: "usbipd",
                table: "PolicyRules",
                column: "ServerName");
        }
    }
}
