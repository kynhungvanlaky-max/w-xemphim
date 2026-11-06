using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebXemPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchNameLower",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_users_SearchNameLower_CreatedAtUtc",
                table: "users",
                columns: new[] { "SearchNameLower", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_reviews_CreatedAtUtc",
                table: "reviews",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_playback_telemetry_SessionId",
                table: "playback_telemetry",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_CreatedAtUtc",
                table: "payments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payments_Status_CreatedAtUtc",
                table: "payments",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_movies_Country",
                table: "movies",
                column: "Country");

            migrationBuilder.CreateIndex(
                name: "IX_movies_Rating",
                table: "movies",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_movies_ViewCount",
                table: "movies",
                column: "ViewCount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_SearchNameLower_CreatedAtUtc",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_reviews_CreatedAtUtc",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "IX_playback_telemetry_SessionId",
                table: "playback_telemetry");

            migrationBuilder.DropIndex(
                name: "IX_payments_CreatedAtUtc",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_Status_CreatedAtUtc",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_movies_Country",
                table: "movies");

            migrationBuilder.DropIndex(
                name: "IX_movies_Rating",
                table: "movies");

            migrationBuilder.DropIndex(
                name: "IX_movies_ViewCount",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "SearchNameLower",
                table: "users");
        }
    }
}
