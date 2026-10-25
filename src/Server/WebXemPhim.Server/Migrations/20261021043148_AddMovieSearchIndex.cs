using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebXemPhim.Server.Migrations
{
    /// <summary>
    /// Installs the PostgreSQL full-text search index for the movie catalog: a weighted
    /// <c>tsvector</c> column, the normalization function that folds Vietnamese diacritics and
    /// the triggers that keep the vector in sync with the movie and cast member rows.
    /// </summary>
    public partial class AddMovieSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The unaccent extension is a trusted extension since PostgreSQL 13, but a managed
            // instance may still refuse to install it. The fallback normalization below keeps
            // the index working instead of aborting the migration.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS unaccent;
EXCEPTION WHEN insufficient_privilege OR undefined_file THEN
    RAISE NOTICE 'unaccent extension is not available; using the ASCII fallback normalization.';
END $$;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION webxemphim_normalize(p_text text)
RETURNS text LANGUAGE plpgsql STABLE PARALLEL SAFE AS $$
DECLARE
  v_text text;
BEGIN
  v_text := coalesce(p_text, '');
  -- The unaccent extension is optional: when it is missing the function still folds the
  -- Vietnamese 'Đ' letter and lowercases the text.
  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'unaccent') THEN
    v_text := public.unaccent(v_text);
  END IF;
  RETURN lower(regexp_replace(
           replace(replace(v_text, U&'\0110', 'D'), U&'\0111', 'd'),
           '\s+', ' ', 'g'));
END;
$$;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION webxemphim_movie_search_vector(p_movie_id integer)
RETURNS tsvector LANGUAGE plpgsql STABLE AS $$
DECLARE
  v_title text;
  v_original_title text;
  v_director text;
  v_overview text;
  v_cast text;
BEGIN
  SELECT m.""Title"", m.""OriginalTitle"", m.""Director"", m.""Overview""
    INTO v_title, v_original_title, v_director, v_overview
    FROM movies m
   WHERE m.""Id"" = p_movie_id;

  IF NOT FOUND THEN
    RETURN NULL;
  END IF;

  SELECT string_agg(c.""Name"", ' ' ORDER BY c.""Position"") INTO v_cast
    FROM cast_members c
   WHERE c.""MovieId"" = p_movie_id;

  RETURN setweight(to_tsvector('simple', webxemphim_normalize(v_title)), 'A')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_original_title)), 'B')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_director)), 'C')
      || setweight(to_tsvector('simple', webxemphim_normalize(coalesce(v_cast, ''))), 'D')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_overview)), 'D');
END;
$$;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION webxemphim_refresh_movie_search_vector()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  UPDATE movies m
     SET ""search_vector"" = webxemphim_movie_search_vector(m.""Id"")
   WHERE m.""Id"" = NEW.""Id"";
  RETURN NULL;
END;
$$;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION webxemphim_refresh_movie_search_vector_for_cast()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  v_movie_id integer;
BEGIN
  v_movie_id := COALESCE(NEW.""MovieId"", OLD.""MovieId"");
  UPDATE movies m
     SET ""search_vector"" = webxemphim_movie_search_vector(m.""Id"")
   WHERE m.""Id"" = v_movie_id;
  RETURN NULL;
END;
$$;");

            migrationBuilder.Sql("ALTER TABLE movies ADD COLUMN IF NOT EXISTS \"search_vector\" tsvector NULL;");

            migrationBuilder.Sql(@"
CREATE OR REPLACE TRIGGER trg_movies_search_vector
AFTER INSERT OR UPDATE OF ""Title"", ""OriginalTitle"", ""Director"", ""Overview"" ON movies FOR EACH ROW
EXECUTE FUNCTION webxemphim_refresh_movie_search_vector();");

            migrationBuilder.Sql(@"
CREATE OR REPLACE TRIGGER trg_cast_movies_search_vector
AFTER INSERT OR UPDATE OR DELETE ON cast_members FOR EACH ROW
EXECUTE FUNCTION webxemphim_refresh_movie_search_vector_for_cast();");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_movies_SearchVector\" ON movies USING GIN (\"search_vector\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_cast_movies_search_vector ON cast_members;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_movies_search_vector ON movies;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_movies_SearchVector;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS webxemphim_refresh_movie_search_vector_for_cast();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS webxemphim_refresh_movie_search_vector();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS webxemphim_movie_search_vector(integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS webxemphim_normalize(text);");
            migrationBuilder.Sql("ALTER TABLE movies DROP COLUMN IF EXISTS \"search_vector\";");
        }
    }
}
