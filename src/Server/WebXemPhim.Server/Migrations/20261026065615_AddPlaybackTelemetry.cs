using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebXemPhim.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "playback_telemetry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovieId = table.Column<int>(type: "integer", nullable: false),
                    EpisodeId = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ServerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Quality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playback_telemetry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_playback_telemetry_CreatedAtUtc",
                table: "playback_telemetry",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_playback_telemetry_EpisodeId",
                table: "playback_telemetry",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_playback_telemetry_EventType_CreatedAtUtc",
                table: "playback_telemetry",
                columns: new[] { "EventType", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "playback_telemetry");
        }
    }
}
