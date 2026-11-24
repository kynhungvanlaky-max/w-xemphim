using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebXemPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardeningSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LoginCooldownUntilUtc",
                table: "user_security",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdminUserId",
                table: "audit_logs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoginCooldownUntilUtc",
                table: "user_security");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "AdminUserId",
                table: "audit_logs");
        }
    }
}
