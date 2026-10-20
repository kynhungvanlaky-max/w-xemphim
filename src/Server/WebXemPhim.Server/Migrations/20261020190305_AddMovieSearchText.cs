using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebXemPhim.Server.Migrations
{
    /// <summary>
    /// Adds the normalized, diacritic-free <c>movies.SearchText</c> column used by
    /// provider-agnostic search. The column type and the supporting index are
    /// provider specific (SQLite vs MySQL), so the migration branches explicitly
    /// instead of relying on the design-time provider.
    /// </summary>
    public partial class AddMovieSearchText : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (IsMySql(migrationBuilder))
            {
                migrationBuilder.AddColumn<string>(
                    name: "SearchText",
                    table: "movies",
                    type: "longtext",
                    nullable: true);

                // MySQL cannot index a LONGTEXT column without a key length.
                migrationBuilder.Sql("CREATE INDEX IX_movies_SearchText ON movies (SearchText(191));");
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "SearchText",
                    table: "movies",
                    type: "TEXT",
                    nullable: true);

                migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_movies_SearchText ON movies (SearchText);");
            }

            // Backfill normalized search text for pre-existing rows.
            migrationBuilder.Sql("UPDATE movies SET SearchText = '' WHERE SearchText IS NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (IsMySql(migrationBuilder))
            {
                migrationBuilder.Sql("DROP INDEX IX_movies_SearchText ON movies;");
            }
            else
            {
                migrationBuilder.Sql("DROP INDEX IF EXISTS IX_movies_SearchText;");
            }

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "movies");
        }

        private static bool IsMySql(MigrationBuilder migrationBuilder) =>
            migrationBuilder.ActiveProvider?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
    }
}
