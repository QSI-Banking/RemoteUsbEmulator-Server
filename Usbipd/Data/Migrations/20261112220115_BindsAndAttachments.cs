using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class BindsAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Binds",
                schema: "usbipd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Binds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Binds_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "usbipd",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PolicyRules",
                schema: "usbipd",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BusId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    HardwareId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                schema: "usbipd",
                columns: table => new
                {
                    BindId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ClientAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StubInstanceId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.BindId);
                    table.ForeignKey(
                        name: "FK_Attachments_Binds_BindId",
                        column: x => x.BindId,
                        principalSchema: "usbipd",
                        principalTable: "Binds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Binds_DeviceId",
                schema: "usbipd",
                table: "Binds",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyRules_ServerName",
                schema: "usbipd",
                table: "PolicyRules",
                column: "ServerName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments",
                schema: "usbipd");

            migrationBuilder.DropTable(
                name: "PolicyRules",
                schema: "usbipd");

            migrationBuilder.DropTable(
                name: "Binds",
                schema: "usbipd");
        }
    }
}
