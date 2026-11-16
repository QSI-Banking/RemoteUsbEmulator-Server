using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBindArrival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArrivedUtc",
                schema: "usbipd",
                table: "Binds",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivedUtc",
                schema: "usbipd",
                table: "Binds");
        }
    }
}
