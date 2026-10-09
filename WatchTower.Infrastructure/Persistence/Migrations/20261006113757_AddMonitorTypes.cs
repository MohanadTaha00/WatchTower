using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WatchTower.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitorTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Monitors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<int>(
                name: "ExpectedStatusCode",
                table: "Monitors",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedBodyContains",
                table: "Monitors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpMethod",
                table: "Monitors",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Monitors",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "Monitors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "Monitors",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHeadersJson",
                table: "Monitors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Monitors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Website");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedBodyContains",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "HttpMethod",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "Port",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "RequestHeadersJson",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Monitors");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Monitors",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ExpectedStatusCode",
                table: "Monitors",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
