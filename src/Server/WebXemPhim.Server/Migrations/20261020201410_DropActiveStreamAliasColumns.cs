using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebXemPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class DropActiveStreamAliasColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "active_streams");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAtUtc",
                table: "active_streams");

            migrationBuilder.CreateTable(
                name: "app_maintenance_flags",
                columns: table => new
                {
                    Flag = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_maintenance_flags", x => x.Flag);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_maintenance_flags");

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "active_streams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAtUtc",
                table: "active_streams",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
