using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class WatchedBinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                schema: "usbipd",
                table: "Binds",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedRunId",
                schema: "usbipd",
                table: "Binds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedUtc",
                schema: "usbipd",
                table: "Binds",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                schema: "usbipd",
                table: "Binds",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HardwareId",
                schema: "usbipd",
                table: "Binds",
                type: "varchar(9)",
                unicode: false,
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Interfaces",
                schema: "usbipd",
                table: "Binds",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationPath",
                schema: "usbipd",
                table: "Binds",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                schema: "usbipd",
                table: "Binds",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServerRuns",
                schema: "usbipd",
                columns: table => new
                {
                    ServerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerRuns", x => x.ServerName);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerRuns",
                schema: "usbipd");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "ConfirmedRunId",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "ConfirmedUtc",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "HardwareId",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "Interfaces",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "LocationPath",
                schema: "usbipd",
                table: "Binds");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                schema: "usbipd",
                table: "Binds");
        }
    }
}
