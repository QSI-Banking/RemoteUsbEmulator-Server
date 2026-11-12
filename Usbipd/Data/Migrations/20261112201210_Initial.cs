using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "usbipd");

            migrationBuilder.CreateTable(
                name: "AdminAudit",
                schema: "usbipd",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Account = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                schema: "usbipd",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    InstanceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "usbipd",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserDevices",
                schema: "usbipd",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDevices", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserDevices_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "usbipd",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserDevices_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "usbipd",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAudit_TimestampUtc",
                schema: "usbipd",
                table: "AdminAudit",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ServerName_InstanceId",
                schema: "usbipd",
                table: "Devices",
                columns: new[] { "ServerName", "InstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_DeviceId",
                schema: "usbipd",
                table: "UserDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Name",
                schema: "usbipd",
                table: "Users",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAudit",
                schema: "usbipd");

            migrationBuilder.DropTable(
                name: "UserDevices",
                schema: "usbipd");

            migrationBuilder.DropTable(
                name: "Devices",
                schema: "usbipd");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "usbipd");
        }
    }
}
