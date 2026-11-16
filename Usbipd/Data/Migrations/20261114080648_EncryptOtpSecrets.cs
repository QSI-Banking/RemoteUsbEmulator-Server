using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usbipd.Data.Migrations
{
    /// <inheritdoc />
    public partial class EncryptOtpSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Secret",
                schema: "usbipd",
                table: "OtpSecrets");

            migrationBuilder.AddColumn<string>(
                name: "CertificateThumbprint",
                schema: "usbipd",
                table: "OtpSecrets",
                type: "char(40)",
                unicode: false,
                fixedLength: true,
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedSecret",
                schema: "usbipd",
                table: "OtpSecrets",
                type: "varchar(4000)",
                unicode: false,
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateThumbprint",
                schema: "usbipd",
                table: "OtpSecrets");

            migrationBuilder.DropColumn(
                name: "ProtectedSecret",
                schema: "usbipd",
                table: "OtpSecrets");

            migrationBuilder.AddColumn<byte[]>(
                name: "Secret",
                schema: "usbipd",
                table: "OtpSecrets",
                type: "varbinary(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
