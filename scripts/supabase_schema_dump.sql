--
-- PostgreSQL database dump
--

\restrict RhQgpFYTQ49z3zfwT6jQoWabkknr1yAy3dDTnVuLiZawCSzTUHAujxdgmL8kfEX

-- Dumped from database version 17.11
-- Dumped by pg_dump version 17.11

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: webxemphim; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA webxemphim;


--
-- Name: webxemphim_movie_search_vector(integer); Type: FUNCTION; Schema: webxemphim; Owner: -
--

CREATE FUNCTION webxemphim.webxemphim_movie_search_vector(p_movie_id integer) RETURNS tsvector
    LANGUAGE plpgsql STABLE
    AS $$
DECLARE
  v_title text;
  v_original_title text;
  v_director text;
  v_overview text;
  v_cast text;
BEGIN
  SELECT m."Title", m."OriginalTitle", m."Director", m."Overview"
    INTO v_title, v_original_title, v_director, v_overview
    FROM movies m
   WHERE m."Id" = p_movie_id;

  IF NOT FOUND THEN
    RETURN NULL;
  END IF;

  SELECT string_agg(c."Name", ' ' ORDER BY c."Position") INTO v_cast
    FROM cast_members c
   WHERE c."MovieId" = p_movie_id;

  RETURN setweight(to_tsvector('simple', webxemphim_normalize(v_title)), 'A')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_original_title)), 'B')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_director)), 'C')
      || setweight(to_tsvector('simple', webxemphim_normalize(coalesce(v_cast, ''))), 'D')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_overview)), 'D');
END;
$$;


--
-- Name: webxemphim_normalize(text); Type: FUNCTION; Schema: webxemphim; Owner: -
--

CREATE FUNCTION webxemphim.webxemphim_normalize(p_text text) RETURNS text
    LANGUAGE plpgsql STABLE PARALLEL SAFE
    AS $_$
DECLARE
  v_text text;
  v_unaccent_schema text;
BEGIN
  v_text := coalesce(p_text, '');
  SELECT n.nspname INTO v_unaccent_schema
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
   WHERE p.proname = 'unaccent'
     AND p.pronargs = 1
   LIMIT 1;
  IF v_unaccent_schema IS NOT NULL THEN
    EXECUTE format('SELECT %I.unaccent($1)', v_unaccent_schema) INTO v_text USING v_text;
  END IF;
  RETURN lower(regexp_replace(
           replace(replace(v_text, U&'\0110', 'D'), U&'\0111', 'd'),
           '\s+', ' ', 'g'));
END;
$_$;


--
-- Name: webxemphim_refresh_movie_search_vector(); Type: FUNCTION; Schema: webxemphim; Owner: -
--

CREATE FUNCTION webxemphim.webxemphim_refresh_movie_search_vector() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  UPDATE movies m
     SET "search_vector" = webxemphim_movie_search_vector(m."Id")
   WHERE m."Id" = NEW."Id";
  RETURN NULL;
END;
$$;


--
-- Name: webxemphim_refresh_movie_search_vector_for_cast(); Type: FUNCTION; Schema: webxemphim; Owner: -
--

CREATE FUNCTION webxemphim.webxemphim_refresh_movie_search_vector_for_cast() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
  v_movie_id integer;
BEGIN
  v_movie_id := COALESCE(NEW."MovieId", OLD."MovieId");
  UPDATE movies m
     SET "search_vector" = webxemphim_movie_search_vector(m."Id")
   WHERE m."Id" = v_movie_id;
  RETURN NULL;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: access_tokens; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.access_tokens (
    "Token" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: active_streams; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.active_streams (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeId" integer,
    "SessionId" character varying(64) NOT NULL,
    "ConnectionId" character varying(64),
    "IpAddress" character varying(45),
    "DeviceInfo" character varying(256),
    "StartedAtUtc" timestamp with time zone NOT NULL,
    "LastHeartbeatUtc" timestamp with time zone NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL
);


--
-- Name: app_maintenance_flags; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.app_maintenance_flags (
    "Flag" character varying(120) NOT NULL,
    "CompletedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: audit_logs; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.audit_logs (
    "Id" uuid NOT NULL,
    "TimestampUtc" timestamp with time zone NOT NULL,
    "AdminEmail" character varying(200) NOT NULL,
    "Action" character varying(80) NOT NULL,
    "EntityType" character varying(80) NOT NULL,
    "EntityId" character varying(120) NOT NULL,
    "Summary" character varying(500) NOT NULL,
    "DetailsJson" text
);


--
-- Name: cast_members; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.cast_members (
    "MovieId" integer NOT NULL,
    "Position" integer NOT NULL,
    "Name" character varying(120) NOT NULL
);


--
-- Name: coupons; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.coupons (
    "Code" character varying(32) NOT NULL,
    "DiscountType" character varying(32) NOT NULL,
    "DiscountValue" numeric(12,2) NOT NULL,
    "ValidUntilUtc" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL
);


--
-- Name: episodes; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.episodes (
    "Id" integer NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeNumber" integer NOT NULL,
    "Title" character varying(240) NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "ThumbnailUrl" character varying(600) NOT NULL,
    "Overview" character varying(2000) NOT NULL
);


--
-- Name: episodes_Id_seq; Type: SEQUENCE; Schema: webxemphim; Owner: -
--

ALTER TABLE webxemphim.episodes ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME webxemphim."episodes_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: genres; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.genres (
    "Id" integer NOT NULL,
    "Name" character varying(80) NOT NULL,
    "Slug" character varying(80) NOT NULL
);


--
-- Name: genres_Id_seq; Type: SEQUENCE; Schema: webxemphim; Owner: -
--

ALTER TABLE webxemphim.genres ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME webxemphim."genres_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: mfa_challenges; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.mfa_challenges (
    "TokenHash" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "FailedAttempts" integer NOT NULL,
    "Version" bigint DEFAULT 0 NOT NULL
);


--
-- Name: mfa_credentials; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.mfa_credentials (
    "UserId" uuid NOT NULL,
    "ProtectedSecret" character varying(1000) NOT NULL,
    "RecoveryCodeHashes" character varying(2000) NOT NULL,
    "Enabled" boolean NOT NULL,
    "EnabledAtUtc" timestamp with time zone,
    "LastUsedTimeStep" bigint,
    "Version" bigint DEFAULT 0 NOT NULL
);


--
-- Name: mfa_verified_sessions; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.mfa_verified_sessions (
    "TokenHash" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "VerifiedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: movie_genres; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.movie_genres (
    "MovieId" integer NOT NULL,
    "GenreId" integer NOT NULL
);


--
-- Name: movies; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.movies (
    "Id" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "OriginalTitle" character varying(200) NOT NULL,
    "Slug" character varying(180) NOT NULL,
    "Overview" character varying(2000) NOT NULL,
    "PosterUrl" character varying(600) NOT NULL,
    "BackdropUrl" character varying(600) NOT NULL,
    "TrailerUrl" character varying(600),
    "Rating" double precision NOT NULL,
    "VoteCount" integer NOT NULL,
    "ReleaseYear" integer NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "AgeRating" character varying(16) NOT NULL,
    "Quality" character varying(32) NOT NULL,
    "Type" integer NOT NULL,
    "Country" character varying(80) NOT NULL,
    "ViewCount" bigint NOT NULL,
    "IsTrending" boolean NOT NULL,
    "IsFeatured" boolean NOT NULL,
    "Director" character varying(120) NOT NULL,
    "SearchText" text,
    "TotalEpisodes" integer NOT NULL,
    "CurrentEpisode" integer NOT NULL,
    search_vector tsvector
);


--
-- Name: movies_Id_seq; Type: SEQUENCE; Schema: webxemphim; Owner: -
--

ALTER TABLE webxemphim.movies ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME webxemphim."movies_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: notifications; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.notifications (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Copy" character varying(1000) NOT NULL,
    "Icon" character varying(32) NOT NULL,
    "ActionUrl" character varying(500),
    "Read" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: password_reset_tokens; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.password_reset_tokens (
    "Token" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "UsedAtUtc" timestamp with time zone
);


--
-- Name: payments; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.payments (
    "Id" uuid NOT NULL,
    "OrderCode" character varying(32) NOT NULL,
    "UserId" uuid NOT NULL,
    "PlanId" character varying(32) NOT NULL,
    "PlanName" character varying(80) NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "Method" integer NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "PaidAtUtc" timestamp with time zone,
    "VipExpiresAtUtc" timestamp with time zone,
    "TransactionReference" character varying(120),
    "Note" text
);


--
-- Name: playback_issues; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.playback_issues (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeId" integer NOT NULL,
    "Description" character varying(800) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: playback_telemetry; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.playback_telemetry (
    "Id" bigint NOT NULL,
    "UserId" uuid,
    "MovieId" integer NOT NULL,
    "EpisodeId" integer NOT NULL,
    "SessionId" character varying(64),
    "Position" integer NOT NULL,
    "ServerName" character varying(120),
    "Quality" character varying(32),
    "EventType" character varying(32) NOT NULL,
    "Value" bigint NOT NULL,
    "ErrorCode" character varying(240),
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: playback_telemetry_Id_seq; Type: SEQUENCE; Schema: webxemphim; Owner: -
--

ALTER TABLE webxemphim.playback_telemetry ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME webxemphim."playback_telemetry_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: reviews; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.reviews (
    "Id" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "UserId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Comment" character varying(800) NOT NULL,
    "AdminReply" text,
    "AdminRepliedAtUtc" timestamp with time zone,
    "IsReported" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: security_audit_events; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.security_audit_events (
    "Id" uuid NOT NULL,
    "UserId" uuid,
    "EventType" character varying(80) NOT NULL,
    "Success" boolean NOT NULL,
    "Detail" character varying(240) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: stream_sources; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.stream_sources (
    "EpisodeId" integer NOT NULL,
    "Position" integer NOT NULL,
    "ServerName" character varying(120) NOT NULL,
    "ServerType" integer NOT NULL,
    "Quality" character varying(32) NOT NULL,
    "StreamUrl" character varying(600) NOT NULL
);


--
-- Name: user_library; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.user_library (
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "AddedAt" timestamp with time zone NOT NULL
);


--
-- Name: user_security; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.user_security (
    "UserId" uuid NOT NULL,
    "IsAdmin" boolean NOT NULL,
    "PasswordIterations" integer NOT NULL,
    "FailedLoginAttempts" integer NOT NULL,
    "LockedUntilUtc" timestamp with time zone,
    "Version" bigint NOT NULL
);


--
-- Name: user_settings; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.user_settings (
    "UserId" uuid NOT NULL,
    "DefaultQuality" character varying(32) NOT NULL,
    "AutoplayNext" boolean NOT NULL,
    "NotifyNewEpisodes" boolean NOT NULL,
    "Language" character varying(10) DEFAULT 'vi'::character varying NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.users (
    "Id" uuid NOT NULL,
    "FullName" character varying(500) NOT NULL,
    "Email" character varying(500) NOT NULL,
    "NormalizedEmail" character varying(160) NOT NULL,
    "EmailLookupHash" character varying(64),
    "PasswordSalt" bytea NOT NULL,
    "PasswordHash" bytea NOT NULL,
    "Membership" integer NOT NULL,
    "VipExpiresAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: vip_plans; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.vip_plans (
    "Id" character varying(32) NOT NULL,
    "Name" character varying(80) NOT NULL,
    "Description" character varying(500) NOT NULL,
    "PriceVnd" numeric(12,2) NOT NULL,
    "DurationDays" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL
);


--
-- Name: watch_progress; Type: TABLE; Schema: webxemphim; Owner: -
--

CREATE TABLE webxemphim.watch_progress (
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeId" integer,
    "EpisodeNumber" integer NOT NULL,
    "ProgressSeconds" integer NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "LastWatchedAtUtc" timestamp with time zone NOT NULL
);


--
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
20261021043129_InitialCreate	10.0.9
20261021043148_AddMovieSearchIndex	10.0.9
20261021052507_AddSecurityAuditEventUserForeignKey	10.0.9
20261026065615_AddPlaybackTelemetry	10.0.9
\.


--
-- Data for Name: access_tokens; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.access_tokens ("Token", "UserId", "ExpiresAtUtc") FROM stdin;
sha256:TIfnZJf1aX7kTD2snF5BJMkxPUJ4nEy0B+7lE/BfA/M=	11111111-1111-1111-1111-111111111111	2026-11-10 13:54:36.252168+00
sha256:mmtGeOXrwQ8VdbWKQJTDQT0ihecObOixY9M5w1PucZE=	11111111-1111-1111-1111-111111111111	2026-11-10 14:15:17.392369+00
sha256:P5XqYXwecWARrdnfoAL63q72Uv+4meW0CACEBRFfAg0=	11111111-1111-1111-1111-111111111111	2026-11-10 18:57:28.513586+00
sha256:eo2OEqKgt5ZSSnhU+m4JRG5Im6Ok5uI7gCeHznImMH0=	11111111-1111-1111-1111-111111111111	2026-11-10 19:09:33.647694+00
sha256:z1mW/yGQOAlwgiDvpBJZnfpHwsm1BIQ02yDORamb3cc=	11111111-1111-1111-1111-111111111111	2026-11-10 19:11:05.476529+00
sha256:bieCRuPmjqZ8hMDmwRs1dlLNjDXwF1q1YGmJ8cfCL4g=	11111111-1111-1111-1111-111111111111	2026-11-11 04:27:57.126274+00
sha256:7Q21BTJ+2BywdQkJpERyWmzNZ3SvCwyfOugxJI+YDzI=	11111111-1111-1111-1111-111111111111	2026-11-11 04:28:36.86135+00
\.


--
-- Data for Name: active_streams; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.active_streams ("Id", "UserId", "MovieId", "EpisodeId", "SessionId", "ConnectionId", "IpAddress", "DeviceInfo", "StartedAtUtc", "LastHeartbeatUtc", "ExpiresAtUtc", "IsActive") FROM stdin;
e675f7a5-8949-4fc2-91c9-5ecd4085d4fd	11111111-1111-1111-1111-111111111111	1	101	1bb39eb8f7e7462bba190aef3531727f	\N	::1	Yêu cầu ký luồng phát	2026-11-03 13:55:41.88311+00	2026-11-03 13:55:41.88311+00	2026-11-03 13:56:41.88311+00	f
e9c9709e-4f9c-4a3c-a82c-61eb226f72b7	11111111-1111-1111-1111-111111111111	1	101	5e7cea9c5c1648ec8f652c2e6186b966	\N	::1	Yêu cầu ký luồng phát	2026-11-03 13:57:27.183951+00	2026-11-03 13:57:27.183951+00	2026-11-03 13:58:27.183951+00	f
4b760c16-20e2-4b95-9063-a6fceb7693e1	11111111-1111-1111-1111-111111111111	1	101	4ac485a8eb1a475cbaeedebdc9dd490d	4rjZbrdoxvG_Ivkez5tijA	::1	Trình duyệt Web	2026-11-03 18:57:32.437092+00	2026-11-03 18:58:02.560361+00	2026-11-03 18:59:02.560361+00	f
64043070-9d2e-4111-9c31-eb39fc099143	11111111-1111-1111-1111-111111111111	1	101	bc13d3fb8500444a9c5bc45e0933a2b7	4rjZbrdoxvG_Ivkez5tijA	::1	Trình duyệt Web	2026-11-03 18:58:05.975879+00	2026-11-03 19:02:05.99865+00	2026-11-03 19:03:05.99865+00	f
32e264ad-9b56-4b91-a4f7-975069520560	11111111-1111-1111-1111-111111111111	1	101	5969d70c196c4ecab5970be6cf55dc6a	5eSx_zt1tb-v4MyaqeOzhQ	::1	Trình duyệt Web	2026-11-03 19:05:50.598788+00	2026-11-03 19:05:50.598788+00	2026-11-03 19:06:50.598788+00	f
c2261c3a-d45b-4bdd-bfdc-48504463a6cd	11111111-1111-1111-1111-111111111111	1	101	a56fb78b27754f8ea47e13454b7fa854	5eSx_zt1tb-v4MyaqeOzhQ	::1	Trình duyệt Web	2026-11-03 19:05:50.726487+00	2026-11-03 19:09:20.780034+00	2026-11-03 19:10:20.780034+00	f
e60feca1-9711-4043-9fb3-8710f057ba34	11111111-1111-1111-1111-111111111111	1	101	3bc04e5b59ce47bc8c45dcf54dcfd322	CQyGc8yYITOQRDdtGe4yBw	::1	Trình duyệt Web	2026-11-03 19:09:38.276253+00	2026-11-03 19:09:38.276253+00	2026-11-03 19:10:38.276253+00	f
16540245-4973-43c6-88c4-6e4e39975f63	11111111-1111-1111-1111-111111111111	1	101	9c2e0c90fce94d4e9e02e164e51b3bd9	CQyGc8yYITOQRDdtGe4yBw	::1	Trình duyệt Web	2026-11-03 19:09:45.407791+00	2026-11-03 19:09:45.407791+00	2026-11-03 19:10:45.407791+00	f
09f0a001-7652-426d-a22d-15d54231ab56	11111111-1111-1111-1111-111111111111	1	101	c62fa386ae8f4b749d77958493d090a0	CQyGc8yYITOQRDdtGe4yBw	::1	Trình duyệt Web	2026-11-03 19:09:57.689342+00	2026-11-03 19:09:57.689342+00	2026-11-03 19:10:57.689342+00	f
ffeb635b-7a5f-4cdd-8b1b-72a9a8800930	11111111-1111-1111-1111-111111111111	1	101	bea704a82b4e4e90af00ab78e76933cb	CQyGc8yYITOQRDdtGe4yBw	::1	Trình duyệt Web	2026-11-03 19:10:03.046553+00	2026-11-03 19:10:03.046553+00	2026-11-03 19:11:03.046553+00	f
ea8ae89f-849d-40bf-b306-a99efb0aefab	11111111-1111-1111-1111-111111111111	1	101	957b944ccbb74ad2911042e8c9daf87e	CQyGc8yYITOQRDdtGe4yBw	::1	Trình duyệt Web	2026-11-03 19:10:28.131713+00	2026-11-03 19:10:28.131713+00	2026-11-03 19:11:28.131713+00	f
ea2010c0-e907-494e-bf5a-a4281d3af085	11111111-1111-1111-1111-111111111111	1	101	dd8a5c366cf94fdc887504471e259f9b	vtNv2sUT2Qw-h5TIFcNjOQ	::1	Trình duyệt Web	2026-11-03 19:11:07.760079+00	2026-11-03 19:11:07.760079+00	2026-11-03 19:12:07.760079+00	f
2c111140-0801-4d91-9143-1faf90ecc555	11111111-1111-1111-1111-111111111111	1	101	1428390b9fa347a2a3e673894719306b	vtNv2sUT2Qw-h5TIFcNjOQ	::1	Trình duyệt Web	2026-11-03 19:11:11.529174+00	2026-11-03 19:11:11.529174+00	2026-11-03 19:12:11.529174+00	f
b296c0a4-ecb8-4465-80a3-295b34633d56	11111111-1111-1111-1111-111111111111	1	101	2b31bb5cb68843a0b4826369b99e544a	cO5fZ6MbCW6vB1KxSavMzw	::1	Trình duyệt Web	2026-11-03 19:11:23.14251+00	2026-11-03 19:11:23.14251+00	2026-11-03 19:12:23.14251+00	f
f4c63970-7fb9-46b3-b547-96f0417605c0	11111111-1111-1111-1111-111111111111	1	101	460aa422e35f4093b77a965b8461264b	aDEI6vgPWDWlfaEs0g8K8A	::1	Trình duyệt Web	2026-11-03 19:11:29.094575+00	2026-11-03 19:11:29.094575+00	2026-11-03 19:12:29.094575+00	f
dd144d2e-d56c-47a7-a2d1-78648bc91372	11111111-1111-1111-1111-111111111111	1	101	3be652873e1e49d2982465584efa97dd	aDEI6vgPWDWlfaEs0g8K8A	::1	Trình duyệt Web	2026-11-03 19:11:29.146213+00	2026-11-03 19:11:29.146213+00	2026-11-03 19:12:29.146213+00	f
272824bd-8b99-4dd7-afec-53dca917b443	11111111-1111-1111-1111-111111111111	1	101	06b513b94ae6439c936cc36ce5976f09	aDEI6vgPWDWlfaEs0g8K8A	::1	Trình duyệt Web	2026-11-03 19:11:41.79657+00	2026-11-03 19:11:41.79657+00	2026-11-03 19:12:41.79657+00	f
373e8e4c-5483-4717-96e1-70000619f412	11111111-1111-1111-1111-111111111111	1	101	0a29759d7e8b434d908384ebc3147294	aDEI6vgPWDWlfaEs0g8K8A	::1	Trình duyệt Web	2026-11-03 19:11:47.850458+00	2026-11-03 19:11:47.850458+00	2026-11-03 19:12:47.850458+00	f
041fda69-8583-4b67-864c-ebc4b4f6fc76	11111111-1111-1111-1111-111111111111	1	101	5dcc45d5d9c34389a9f194d26a384cd0	aDEI6vgPWDWlfaEs0g8K8A	::1	Trình duyệt Web	2026-11-03 19:11:59.66231+00	2026-11-03 19:12:29.686639+00	2026-11-03 19:13:29.686639+00	f
5ca3fdcb-12c2-409a-855a-c830566129f3	11111111-1111-1111-1111-111111111111	1	101	4cb37445da904beda36c6b4cd41a5270	5eSx_zt1tb-v4MyaqeOzhQ	::1	Trình duyệt Web	2026-11-03 19:09:25.139175+00	2026-11-03 19:12:25.154991+00	2026-11-03 19:13:25.154991+00	f
c2560b36-2c15-465f-a255-784391093cd0	11111111-1111-1111-1111-111111111111	1	101	98775a071614404e92259a062b3505e3	CQyGc8yYITOQRDdtGe4yBw	::1	Trình duyệt Web	2026-11-03 19:10:34.047654+00	2026-11-03 19:12:34.060114+00	2026-11-03 19:13:34.060114+00	f
a9e845fc-7d63-440f-b040-a7583208ac22	11111111-1111-1111-1111-111111111111	1	101	71f8e830aec8457a8a28b0410fca1edd	RKcToxC-WUq3QmxCL2pMiA	::1	Trình duyệt Web	2026-11-04 04:27:59.795732+00	2026-11-04 04:27:59.795732+00	2026-11-04 04:28:59.795732+00	f
d7a9b625-cfb8-4c2f-892e-036c9b8ca3fb	11111111-1111-1111-1111-111111111111	1	101	3e042b998bff4f4c9af2cd4819538ac0	RKcToxC-WUq3QmxCL2pMiA	::1	Trình duyệt Web	2026-11-04 04:28:11.41123+00	2026-11-04 04:28:11.41123+00	2026-11-04 04:29:11.41123+00	f
63bbaacb-8f01-44ab-b426-fdc6e39f10a2	11111111-1111-1111-1111-111111111111	1	101	0f0ee80165004f5bb0f025173335e299	7Bf0pyrBQul5g2wpPUGhxg	::1	Trình duyệt Web	2026-11-04 04:28:38.419733+00	2026-11-04 04:28:38.419733+00	2026-11-04 04:29:38.419733+00	f
4462a609-8ba9-473a-b93f-f22a3b020a08	11111111-1111-1111-1111-111111111111	1	101	b4abcda8cf5e4434ae3977dcefeda38c	7Bf0pyrBQul5g2wpPUGhxg	::1	Trình duyệt Web	2026-11-04 04:28:40.348723+00	2026-11-04 04:28:40.348723+00	2026-11-04 04:29:40.348723+00	f
09e86037-24d0-48cf-b548-b93d4013747e	11111111-1111-1111-1111-111111111111	1	101	1ac4143820114d15a6623d40d0f007e7	7Bf0pyrBQul5g2wpPUGhxg	::1	Trình duyệt Web	2026-11-04 04:29:06.106408+00	2026-11-04 04:36:36.13045+00	2026-11-04 04:37:36.13045+00	f
62fef2e3-6532-43cc-925f-c8dddb05e9f2	11111111-1111-1111-1111-111111111111	1	101	72bcd43399a346c8b76a8ac97d38134b	RKcToxC-WUq3QmxCL2pMiA	::1	Trình duyệt Web	2026-11-04 04:28:24.260397+00	2026-11-04 04:36:24.280398+00	2026-11-04 04:37:24.280398+00	f
\.


--
-- Data for Name: app_maintenance_flags; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.app_maintenance_flags ("Flag", "CompletedAtUtc") FROM stdin;
legacy-plaintext-tokens-revoked	2026-11-03 13:52:07.26925+00
movie-image-urls-migrated-to-webp	2026-11-03 13:52:07.391565+00
mojibake-text-repaired-v2	2026-11-03 13:52:07.440904+00
\.


--
-- Data for Name: audit_logs; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.audit_logs ("Id", "TimestampUtc", "AdminEmail", "Action", "EntityType", "EntityId", "Summary", "DetailsJson") FROM stdin;
\.


--
-- Data for Name: cast_members; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.cast_members ("MovieId", "Position", "Name") FROM stdin;
1	1	Timothée Chalamet
1	2	Zendaya
1	3	Florence Pugh
1	4	Javier Bardem
1	5	Anya Taylor-Joy
2	1	Cillian Murphy
2	2	Emily Blunt
2	3	Matt Damon
2	4	Robert Downey Jr.
3	1	Hailee Steinfeld
3	2	Ella Purnell
3	3	Katie Leung
4	1	Christian Bale
4	2	Heath Ledger
4	3	Aaron Eckhart
4	4	Gary Oldman
5	1	Taito Ban
5	2	Genta Nakamura
5	3	Reina Ueda
6	1	Hiroyuki Sanada
6	2	Cosmo Jarvis
6	3	Anna Sawai
6	4	Tadanobu Asano
7	1	Matthew McConaughey
7	2	Anne Hathaway
7	3	Jessica Chastain
7	4	Michael Caine
8	1	Millie Bobby Brown
8	2	Finn Wolfhard
8	3	Winona Ryder
8	4	David Harbour
9	1	Leonardo DiCaprio
9	2	Joseph Gordon-Levitt
9	3	Elliot Page
9	4	Tom Hardy
10	1	Emma D'Arcy
10	2	Matt Smith
10	3	Olivia Cooke
10	4	Rhys Ifans
11	1	Song Kang-ho
11	2	Lee Sun-kyun
11	3	Cho Yeo-jeong
11	4	Choi Woo-shik
12	1	Junya Enoki
12	2	Yuma Uchida
12	3	Asami Seto
12	4	Yuichi Nakamura
13	1	Phương Anh Đào
13	2	Tuấn Trần
13	3	Hồng Đào
13	4	Uyển Ân
14	1	Thanh Hiền
14	2	Trương Minh Cường
14	3	Đinh Y Nhung
14	4	Quách Ngọc Tuyên
15	1	Quang Tuấn
15	2	Nam Thư
15	3	NSND Kim Xuân
15	4	Vân Dung
16	1	Hạo Khang
16	2	Tuấn Trần
16	3	Trấn Thành
16	4	Tiến Luật
17	1	Choi Min-sik
17	2	Kim Go-eun
17	3	Yoo Hae-jin
17	4	Lee Do-hyun
18	1	Lee Jung-jae
18	2	Lee Byung-hun
18	3	Wi Ha-joon
18	4	Yim Si-wan
19	1	Kim Soo-hyun
19	2	Kim Ji-won
19	3	Park Sung-hoon
19	4	Kwak Dong-yeon
20	1	Gong Yoo
20	2	Jung Yu-mi
20	3	Ma Dong-seok
20	4	Kim Su-an
21	1	Keanu Reeves
21	2	Donnie Yen
21	3	Bill Skarsgård
21	4	Hiroyuki Sanada
22	1	Shameik Moore
22	2	Hailee Steinfeld
22	3	Oscar Isaac
22	4	Daniel Kaluuya
23	1	Tom Cruise
23	2	Miles Teller
23	3	Jennifer Connelly
23	4	Jon Hamm
24	1	Sam Worthington
24	2	Zoe Saldana
24	3	Sigourney Weaver
24	4	Stephen Lang
25	1	Pedro Pascal
25	2	Bella Ramsey
25	3	Gabriel Luna
25	4	Anna Torv
26	1	Bryan Cranston
26	2	Aaron Paul
26	3	Anna Gunn
26	4	Bob Odenkirk
27	1	Trương Dịch
27	2	Trương Tụng Văn
27	3	Lý Nhất Đồng
27	4	Trương Chí Kiên
28	1	Lưu Diệc Phi
28	2	Lý Hiện
28	3	Hồ Băng Khanh
28	4	Ngưu Tuấn Phong
29	1	Robert Pattinson
29	2	Zoë Kravitz
29	3	Paul Dano
29	4	Jeffrey Wright
30	1	Phí Tường
30	2	Lý Tuyết Kiện
30	3	Hoàng Bột
30	4	Naran
31	1	Natsuki Hanae
31	2	Akari Kito
31	3	Hiro Shimono
31	4	Yoshitsugu Matsuoka
32	1	Yuki Kaji
32	2	Yui Ishikawa
32	3	Marina Inoue
32	4	Hiroshi Kamiya
33	1	Atsumi Tanezaki
33	2	Nobuhiko Okamoto
33	3	Hiroki Touchi
33	4	Kana Ichinose
34	1	Kikunosuke Toya
34	2	Tomori Kusunoki
34	3	Shogo Sakata
34	4	Fairouz Ai
35	1	Ryunosuke Kamiki
35	2	Mone Kamishiraishi
35	3	Ryo Narita
36	1	Rumi Hiiragi
36	2	Miyu Irino
36	3	Mari Natsuki
36	4	Takashi Naito
37	1	Mara Ellis
37	2	Jonas Vale
37	3	Kei Tanaka
37	4	Amara Cole
38	1	Trần Lam
38	2	Nguyễn Hoàng Phúc
38	3	Bảo Anh
38	4	Khánh Vân
39	1	Riku Aoyama
39	2	Mio Kisaragi
39	3	Daichi Mori
39	4	Hana Ueda
40	1	Kim Ji-won
40	2	Lee Do-hyun
40	3	Park Min-young
40	4	Jung Hae-in
\.


--
-- Data for Name: coupons; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.coupons ("Code", "DiscountType", "DiscountValue", "ValidUntilUtc", "IsActive") FROM stdin;
CINEMA2026	percent	20.00	2031-11-03 13:52:07.597434+00	t
TESTVIP	percent	50.00	2031-11-03 13:52:07.597434+00	t
TRIAN50K	fixed	50000.00	2031-11-03 13:52:07.597434+00	t
VIPPRO	percent	15.00	2031-11-03 13:52:07.597434+00	t
\.


--
-- Data for Name: episodes; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.episodes ("Id", "MovieId", "EpisodeNumber", "Title", "DurationMinutes", "ThumbnailUrl", "Overview") FROM stdin;
304	3	4	Tập 4: Arcane: Liên Minh Huyền Thoại (Mùa 2)	44	/media/episodes/ep-3-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
305	3	5	Tập 5: Arcane: Liên Minh Huyền Thoại (Mùa 2)	44	/media/episodes/ep-3-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
306	3	6	Tập 6: Arcane: Liên Minh Huyền Thoại (Mùa 2)	44	/media/episodes/ep-3-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
307	3	7	Tập 7: Arcane: Liên Minh Huyền Thoại (Mùa 2)	44	/media/episodes/ep-3-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
308	3	8	Tập 8: Arcane: Liên Minh Huyền Thoại (Mùa 2)	44	/media/episodes/ep-3-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
309	3	9	Tập 9: Arcane: Liên Minh Huyền Thoại (Mùa 2)	44	/media/episodes/ep-3-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
4101	41	1	Bản Hướng Dẫn Chi Tiết (185 MB)	25	/images/movies/movie-1-backdrop.webp	Hướng dẫn từng bước cài đặt Clean UI và xoá app rác Android TV.
4201	42	1	Video Xu Hướng TikTok (12.6 MB)	15	/images/movies/movie-2-backdrop.webp	Video clip ngắn xu hướng tuyển dụng việc làm Hà Nam 2026.
503	5	3	Tập 3: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
504	5	4	Tập 4: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
505	5	5	Tập 5: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
506	5	6	Tập 6: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
507	5	7	Tập 7: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
508	5	8	Tập 8: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
509	5	9	Tập 9: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
510	5	10	Tập 10: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
511	5	11	Tập 11: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
512	5	12	Tập 12: Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	24	/media/episodes/ep-5-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
4301	43	1	Clip Ngắn Đặc Sắc (3.9 MB)	5	/images/movies/movie-3-backdrop.webp	Clip video ngắn tuyển chọn chất lượng cao.
4401	44	1	Bản Chiếu Đầy Đủ (72.8 MB)	45	/images/movies/movie-4-backdrop.webp	Bản phim chuẩn điện ảnh lưu trữ trên Cloudflare R2.
603	6	3	Tập 3: Shogun: Tướng Quân	60	/media/episodes/ep-6-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
604	6	4	Tập 4: Shogun: Tướng Quân	60	/media/episodes/ep-6-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
605	6	5	Tập 5: Shogun: Tướng Quân	60	/media/episodes/ep-6-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
606	6	6	Tập 6: Shogun: Tướng Quân	60	/media/episodes/ep-6-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
607	6	7	Tập 7: Shogun: Tướng Quân	60	/media/episodes/ep-6-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
608	6	8	Tập 8: Shogun: Tướng Quân	60	/media/episodes/ep-6-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
609	6	9	Tập 9: Shogun: Tướng Quân	60	/media/episodes/ep-6-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
610	6	10	Tập 10: Shogun: Tướng Quân	60	/media/episodes/ep-6-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
802	8	2	Tập 2: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
803	8	3	Tập 3: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
401	4	1	Bản Remaster 4K UHD	152	/media/episodes/ep-4-01.webp	
804	8	4	Tập 4: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
805	8	5	Tập 5: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
806	8	6	Tập 6: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
807	8	7	Tập 7: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
808	8	8	Tập 8: Cậu Bé Mất Tích (Stranger Things Mùa 5)	65	/media/episodes/ep-8-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
4501	45	1	Bản Thu Gốc Meyo (4.2 MB)	4	/images/movies/movie-5-backdrop.webp	Bản thu âm thanh chất lượng cao kèm lời bài hát.
1002	10	2	Tập 2: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1003	10	3	Tập 3: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1004	10	4	Tập 4: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1005	10	5	Tập 5: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1006	10	6	Tập 6: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1007	10	7	Tập 7: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1008	10	8	Tập 8: Gia Tộc Rồng (House of the Dragon Mùa 2)	62	/media/episodes/ep-10-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1202	12	2	Tập 2: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1203	12	3	Tập 3: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1204	12	4	Tập 4: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1205	12	5	Tập 5: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1206	12	6	Tập 6: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1207	12	7	Tập 7: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1208	12	8	Tập 8: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1209	12	9	Tập 9: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1210	12	10	Tập 10: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1211	12	11	Tập 11: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1212	12	12	Tập 12: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1213	12	13	Tập 13: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-13.webp	Diễn biến tập 13 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1214	12	14	Tập 14: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-14.webp	Diễn biến tập 14 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1215	12	15	Tập 15: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-15.webp	Diễn biến tập 15 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1216	12	16	Tập 16: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-16.webp	Diễn biến tập 16 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1217	12	17	Tập 17: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-17.webp	Diễn biến tập 17 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1218	12	18	Tập 18: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-18.webp	Diễn biến tập 18 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1219	12	19	Tập 19: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-19.webp	Diễn biến tập 19 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1220	12	20	Tập 20: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-20.webp	Diễn biến tập 20 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
901	9	1	Bản Chuẩn Điện Ảnh	148	/media/episodes/ep-9-01.webp	
1221	12	21	Tập 21: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-21.webp	Diễn biến tập 21 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1222	12	22	Tập 22: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-22.webp	Diễn biến tập 22 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1223	12	23	Tập 23: Chú Thuật Hồi Chiến: Biến Cố Shibuya	24	/media/episodes/ep-12-23.webp	Diễn biến tập 23 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1802	18	2	Tập 2: Squid Game: Trò Chơi Con Mực (Mùa 2)	55	/media/episodes/ep-18-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1803	18	3	Tập 3: Squid Game: Trò Chơi Con Mực (Mùa 2)	55	/media/episodes/ep-18-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1804	18	4	Tập 4: Squid Game: Trò Chơi Con Mực (Mùa 2)	55	/media/episodes/ep-18-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1805	18	5	Tập 5: Squid Game: Trò Chơi Con Mực (Mùa 2)	55	/media/episodes/ep-18-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1806	18	6	Tập 6: Squid Game: Trò Chơi Con Mực (Mùa 2)	55	/media/episodes/ep-18-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1902	19	2	Tập 2: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1903	19	3	Tập 3: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1904	19	4	Tập 4: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1905	19	5	Tập 5: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1906	19	6	Tập 6: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1907	19	7	Tập 7: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1908	19	8	Tập 8: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1909	19	9	Tập 9: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1910	19	10	Tập 10: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1911	19	11	Tập 11: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1912	19	12	Tập 12: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1913	19	13	Tập 13: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-13.webp	Diễn biến tập 13 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1914	19	14	Tập 14: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-14.webp	Diễn biến tập 14 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1915	19	15	Tập 15: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-15.webp	Diễn biến tập 15 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1916	19	16	Tập 16: Nữ Hoàng Nước Mắt (Queen of Tears)	75	/media/episodes/ep-19-16.webp	Diễn biến tập 16 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2502	25	2	Tập 2: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2503	25	3	Tập 3: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2504	25	4	Tập 4: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2505	25	5	Tập 5: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
1301	13	1	Bản Chiếu Rạp Chính Thức	131	/media/episodes/ep-13-01.webp	
2506	25	6	Tập 6: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2507	25	7	Tập 7: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2508	25	8	Tập 8: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2509	25	9	Tập 9: The Last of Us: Những Người Còn Sót Lại	81	/media/episodes/ep-25-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2602	26	2	Tập 2: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2603	26	3	Tập 3: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2604	26	4	Tập 4: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2605	26	5	Tập 5: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2606	26	6	Tập 6: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2607	26	7	Tập 7: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2608	26	8	Tập 8: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2609	26	9	Tập 9: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2610	26	10	Tập 10: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2611	26	11	Tập 11: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2612	26	12	Tập 12: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2613	26	13	Tập 13: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-13.webp	Diễn biến tập 13 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2614	26	14	Tập 14: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-14.webp	Diễn biến tập 14 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2615	26	15	Tập 15: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-15.webp	Diễn biến tập 15 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2616	26	16	Tập 16: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-16.webp	Diễn biến tập 16 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2617	26	17	Tập 17: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-17.webp	Diễn biến tập 17 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2618	26	18	Tập 18: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-18.webp	Diễn biến tập 18 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2619	26	19	Tập 19: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-19.webp	Diễn biến tập 19 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2620	26	20	Tập 20: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-20.webp	Diễn biến tập 20 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2621	26	21	Tập 21: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-21.webp	Diễn biến tập 21 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2622	26	22	Tập 22: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-22.webp	Diễn biến tập 22 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2623	26	23	Tập 23: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-23.webp	Diễn biến tập 23 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2624	26	24	Tập 24: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-24.webp	Diễn biến tập 24 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2625	26	25	Tập 25: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-25.webp	Diễn biến tập 25 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2626	26	26	Tập 26: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-26.webp	Diễn biến tập 26 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2627	26	27	Tập 27: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-27.webp	Diễn biến tập 27 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2628	26	28	Tập 28: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-28.webp	Diễn biến tập 28 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2629	26	29	Tập 29: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-29.webp	Diễn biến tập 29 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2630	26	30	Tập 30: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-30.webp	Diễn biến tập 30 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2631	26	31	Tập 31: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-31.webp	Diễn biến tập 31 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2632	26	32	Tập 32: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-32.webp	Diễn biến tập 32 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2633	26	33	Tập 33: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-33.webp	Diễn biến tập 33 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2634	26	34	Tập 34: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-34.webp	Diễn biến tập 34 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2635	26	35	Tập 35: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-35.webp	Diễn biến tập 35 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2636	26	36	Tập 36: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-36.webp	Diễn biến tập 36 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2637	26	37	Tập 37: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-37.webp	Diễn biến tập 37 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2638	26	38	Tập 38: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-38.webp	Diễn biến tập 38 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2639	26	39	Tập 39: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-39.webp	Diễn biến tập 39 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2640	26	40	Tập 40: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-40.webp	Diễn biến tập 40 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2641	26	41	Tập 41: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-41.webp	Diễn biến tập 41 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2642	26	42	Tập 42: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-42.webp	Diễn biến tập 42 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2643	26	43	Tập 43: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-43.webp	Diễn biến tập 43 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2644	26	44	Tập 44: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-44.webp	Diễn biến tập 44 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2645	26	45	Tập 45: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-45.webp	Diễn biến tập 45 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2646	26	46	Tập 46: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-46.webp	Diễn biến tập 46 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2647	26	47	Tập 47: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-47.webp	Diễn biến tập 47 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2648	26	48	Tập 48: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-48.webp	Diễn biến tập 48 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2649	26	49	Tập 49: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-49.webp	Diễn biến tập 49 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2650	26	50	Tập 50: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-50.webp	Diễn biến tập 50 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2651	26	51	Tập 51: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-51.webp	Diễn biến tập 51 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2652	26	52	Tập 52: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-52.webp	Diễn biến tập 52 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2653	26	53	Tập 53: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-53.webp	Diễn biến tập 53 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2654	26	54	Tập 54: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-54.webp	Diễn biến tập 54 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2655	26	55	Tập 55: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-55.webp	Diễn biến tập 55 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2656	26	56	Tập 56: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-56.webp	Diễn biến tập 56 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2657	26	57	Tập 57: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-57.webp	Diễn biến tập 57 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2658	26	58	Tập 58: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-58.webp	Diễn biến tập 58 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2659	26	59	Tập 59: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-59.webp	Diễn biến tập 59 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2660	26	60	Tập 60: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-60.webp	Diễn biến tập 60 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2661	26	61	Tập 61: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-61.webp	Diễn biến tập 61 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2662	26	62	Tập 62: Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	58	/media/episodes/ep-26-62.webp	Diễn biến tập 62 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2702	27	2	Tập 2: Cuồng Phong	45	/media/episodes/ep-27-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2703	27	3	Tập 3: Cuồng Phong	45	/media/episodes/ep-27-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2704	27	4	Tập 4: Cuồng Phong	45	/media/episodes/ep-27-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2705	27	5	Tập 5: Cuồng Phong	45	/media/episodes/ep-27-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2706	27	6	Tập 6: Cuồng Phong	45	/media/episodes/ep-27-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2707	27	7	Tập 7: Cuồng Phong	45	/media/episodes/ep-27-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2708	27	8	Tập 8: Cuồng Phong	45	/media/episodes/ep-27-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2709	27	9	Tập 9: Cuồng Phong	45	/media/episodes/ep-27-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2710	27	10	Tập 10: Cuồng Phong	45	/media/episodes/ep-27-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2711	27	11	Tập 11: Cuồng Phong	45	/media/episodes/ep-27-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2712	27	12	Tập 12: Cuồng Phong	45	/media/episodes/ep-27-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2713	27	13	Tập 13: Cuồng Phong	45	/media/episodes/ep-27-13.webp	Diễn biến tập 13 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2714	27	14	Tập 14: Cuồng Phong	45	/media/episodes/ep-27-14.webp	Diễn biến tập 14 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2715	27	15	Tập 15: Cuồng Phong	45	/media/episodes/ep-27-15.webp	Diễn biến tập 15 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2716	27	16	Tập 16: Cuồng Phong	45	/media/episodes/ep-27-16.webp	Diễn biến tập 16 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2717	27	17	Tập 17: Cuồng Phong	45	/media/episodes/ep-27-17.webp	Diễn biến tập 17 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2718	27	18	Tập 18: Cuồng Phong	45	/media/episodes/ep-27-18.webp	Diễn biến tập 18 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2719	27	19	Tập 19: Cuồng Phong	45	/media/episodes/ep-27-19.webp	Diễn biến tập 19 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2720	27	20	Tập 20: Cuồng Phong	45	/media/episodes/ep-27-20.webp	Diễn biến tập 20 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2721	27	21	Tập 21: Cuồng Phong	45	/media/episodes/ep-27-21.webp	Diễn biến tập 21 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2722	27	22	Tập 22: Cuồng Phong	45	/media/episodes/ep-27-22.webp	Diễn biến tập 22 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2723	27	23	Tập 23: Cuồng Phong	45	/media/episodes/ep-27-23.webp	Diễn biến tập 23 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2724	27	24	Tập 24: Cuồng Phong	45	/media/episodes/ep-27-24.webp	Diễn biến tập 24 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2725	27	25	Tập 25: Cuồng Phong	45	/media/episodes/ep-27-25.webp	Diễn biến tập 25 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2726	27	26	Tập 26: Cuồng Phong	45	/media/episodes/ep-27-26.webp	Diễn biến tập 26 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2727	27	27	Tập 27: Cuồng Phong	45	/media/episodes/ep-27-27.webp	Diễn biến tập 27 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2728	27	28	Tập 28: Cuồng Phong	45	/media/episodes/ep-27-28.webp	Diễn biến tập 28 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2729	27	29	Tập 29: Cuồng Phong	45	/media/episodes/ep-27-29.webp	Diễn biến tập 29 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2730	27	30	Tập 30: Cuồng Phong	45	/media/episodes/ep-27-30.webp	Diễn biến tập 30 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2731	27	31	Tập 31: Cuồng Phong	45	/media/episodes/ep-27-31.webp	Diễn biến tập 31 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2732	27	32	Tập 32: Cuồng Phong	45	/media/episodes/ep-27-32.webp	Diễn biến tập 32 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2733	27	33	Tập 33: Cuồng Phong	45	/media/episodes/ep-27-33.webp	Diễn biến tập 33 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2734	27	34	Tập 34: Cuồng Phong	45	/media/episodes/ep-27-34.webp	Diễn biến tập 34 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2735	27	35	Tập 35: Cuồng Phong	45	/media/episodes/ep-27-35.webp	Diễn biến tập 35 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2736	27	36	Tập 36: Cuồng Phong	45	/media/episodes/ep-27-36.webp	Diễn biến tập 36 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2737	27	37	Tập 37: Cuồng Phong	45	/media/episodes/ep-27-37.webp	Diễn biến tập 37 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2701	27	1	Tập 1: Đêm Trừ Tịch	45	/media/episodes/ep-27-01.webp	
2738	27	38	Tập 38: Cuồng Phong	45	/media/episodes/ep-27-38.webp	Diễn biến tập 38 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2739	27	39	Tập 39: Cuồng Phong	45	/media/episodes/ep-27-39.webp	Diễn biến tập 39 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2802	28	2	Tập 2: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2803	28	3	Tập 3: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2804	28	4	Tập 4: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2805	28	5	Tập 5: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2806	28	6	Tập 6: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2807	28	7	Tập 7: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2808	28	8	Tập 8: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2809	28	9	Tập 9: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2810	28	10	Tập 10: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2811	28	11	Tập 11: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2812	28	12	Tập 12: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2813	28	13	Tập 13: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-13.webp	Diễn biến tập 13 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2814	28	14	Tập 14: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-14.webp	Diễn biến tập 14 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2815	28	15	Tập 15: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-15.webp	Diễn biến tập 15 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2816	28	16	Tập 16: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-16.webp	Diễn biến tập 16 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2817	28	17	Tập 17: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-17.webp	Diễn biến tập 17 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2818	28	18	Tập 18: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-18.webp	Diễn biến tập 18 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2819	28	19	Tập 19: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-19.webp	Diễn biến tập 19 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2820	28	20	Tập 20: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-20.webp	Diễn biến tập 20 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2821	28	21	Tập 21: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-21.webp	Diễn biến tập 21 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2822	28	22	Tập 22: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-22.webp	Diễn biến tập 22 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2823	28	23	Tập 23: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-23.webp	Diễn biến tập 23 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2824	28	24	Tập 24: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-24.webp	Diễn biến tập 24 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2825	28	25	Tập 25: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-25.webp	Diễn biến tập 25 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2826	28	26	Tập 26: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-26.webp	Diễn biến tập 26 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2827	28	27	Tập 27: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-27.webp	Diễn biến tập 27 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2828	28	28	Tập 28: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-28.webp	Diễn biến tập 28 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2829	28	29	Tập 29: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-29.webp	Diễn biến tập 29 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2830	28	30	Tập 30: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-30.webp	Diễn biến tập 30 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2831	28	31	Tập 31: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-31.webp	Diễn biến tập 31 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2832	28	32	Tập 32: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-32.webp	Diễn biến tập 32 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2833	28	33	Tập 33: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-33.webp	Diễn biến tập 33 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2834	28	34	Tập 34: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-34.webp	Diễn biến tập 34 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2835	28	35	Tập 35: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-35.webp	Diễn biến tập 35 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2801	28	1	Tập 1: Khởi Hành Đến Đại Lý	45	/media/episodes/ep-28-01.webp	
2836	28	36	Tập 36: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-36.webp	Diễn biến tập 36 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2837	28	37	Tập 37: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-37.webp	Diễn biến tập 37 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2838	28	38	Tập 38: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-38.webp	Diễn biến tập 38 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2839	28	39	Tập 39: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-39.webp	Diễn biến tập 39 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2840	28	40	Tập 40: Đi Đến Nơi Có Gió	45	/media/episodes/ep-28-40.webp	Diễn biến tập 40 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3102	31	2	Tập 2: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3103	31	3	Tập 3: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3104	31	4	Tập 4: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3105	31	5	Tập 5: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3106	31	6	Tập 6: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3107	31	7	Tập 7: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3108	31	8	Tập 8: Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	48	/media/episodes/ep-31-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3202	32	2	Tập 2: Đại Chiến Titan: Hồi Kết Tuyệt Đối	61	/media/episodes/ep-32-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3302	33	2	Tập 2: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3303	33	3	Tập 3: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3304	33	4	Tập 4: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3305	33	5	Tập 5: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3306	33	6	Tập 6: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3307	33	7	Tập 7: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3308	33	8	Tập 8: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3309	33	9	Tập 9: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3310	33	10	Tập 10: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3311	33	11	Tập 11: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3312	33	12	Tập 12: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3313	33	13	Tập 13: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-13.webp	Diễn biến tập 13 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3314	33	14	Tập 14: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-14.webp	Diễn biến tập 14 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3315	33	15	Tập 15: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-15.webp	Diễn biến tập 15 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3316	33	16	Tập 16: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-16.webp	Diễn biến tập 16 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3317	33	17	Tập 17: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-17.webp	Diễn biến tập 17 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3318	33	18	Tập 18: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-18.webp	Diễn biến tập 18 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3319	33	19	Tập 19: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-19.webp	Diễn biến tập 19 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3320	33	20	Tập 20: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-20.webp	Diễn biến tập 20 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
2901	29	1	Bản Chuẩn 4K	176	/media/episodes/ep-29-01.webp	
3321	33	21	Tập 21: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-21.webp	Diễn biến tập 21 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3322	33	22	Tập 22: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-22.webp	Diễn biến tập 22 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3323	33	23	Tập 23: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-23.webp	Diễn biến tập 23 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3324	33	24	Tập 24: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-24.webp	Diễn biến tập 24 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3325	33	25	Tập 25: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-25.webp	Diễn biến tập 25 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3326	33	26	Tập 26: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-26.webp	Diễn biến tập 26 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3327	33	27	Tập 27: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-27.webp	Diễn biến tập 27 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3328	33	28	Tập 28: Frieren: Pháp Sư Tiễn Táng	24	/media/episodes/ep-33-28.webp	Diễn biến tập 28 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3402	34	2	Tập 2: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-02.webp	Diễn biến tập 2 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3403	34	3	Tập 3: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-03.webp	Diễn biến tập 3 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3404	34	4	Tập 4: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-04.webp	Diễn biến tập 4 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3405	34	5	Tập 5: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-05.webp	Diễn biến tập 5 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3406	34	6	Tập 6: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-06.webp	Diễn biến tập 6 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3407	34	7	Tập 7: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-07.webp	Diễn biến tập 7 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3408	34	8	Tập 8: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-08.webp	Diễn biến tập 8 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3409	34	9	Tập 9: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-09.webp	Diễn biến tập 9 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3410	34	10	Tập 10: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-10.webp	Diễn biến tập 10 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3411	34	11	Tập 11: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-11.webp	Diễn biến tập 11 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3412	34	12	Tập 12: Thợ Săn Quỷ Chainsaw Man	24	/media/episodes/ep-34-12.webp	Diễn biến tập 12 của bộ phim, tình tiết mới cập nhật mỗi kỳ chiếu xung đột mới.
3401	34	1	Tập 1: Chó Và Cưa	24	/media/episodes/ep-34-01.webp	
3501	35	1	Bản Điện Ảnh Full HD	106	/media/episodes/ep-35-01.webp	
101	1	1	Bản Chiếu Rạp Đầy Đủ (Director's Cut)	168	/media/episodes/ep-1-01.webp	Toàn bộ diễn biến hoành tráng của phần phim kết thúc bộ ba thiên anh hùng ca Xứ Cát.
201	2	1	Bản Chiếu IMAX	180	/media/episodes/ep-2-01.webp	Kiệt tác điện ảnh đạt 7 giải Oscar của đạo diễn Christopher Nolan.
301	3	1	Tập 1: Mảnh Vỡ Quá Khứ	44	/media/episodes/ep-3-01.webp	Khởi đầu bi kịch mới tại Piltover sau vụ tấn công vào hội đồng.
302	3	2	Tập 2: Báo Động Đỏ	46	/media/episodes/ep-3-02.webp	Vi gia nhập cảnh vệ Piltover để truy lùng Jinx.
303	3	3	Tập 3: Mắt Bão Zaun	48	/media/episodes/ep-3-03.webp	Cuộc đụng độ nghẹt thở giữa các phe phái bóng tối.
501	5	1	Tập 1: Đội Quân Trỗi Dậy	24	/media/episodes/ep-5-01.webp	
502	5	2	Tập 2: Lãnh Địa Quỷ Đỏ	24	/media/episodes/ep-5-02.webp	
601	6	1	Tập 1: Kẻ Ngoại Quốc	60	/media/episodes/ep-6-01.webp	
602	6	2	Tập 2: Đầy Tớ Của Hai Chủ	58	/media/episodes/ep-6-02.webp	
701	7	1	Bản Chiếu Đầy Đủ	169	/media/episodes/ep-7-01.webp	
801	8	1	Tập 1: Cuộc Thu Thập	65	/media/episodes/ep-8-01.webp	
1001	10	1	Tập 1: Nợ Máu Phải Trả Bằng Máu	62	/media/episodes/ep-10-01.webp	
1101	11	1	Bản Chiếu Rạp Đoạt Giải Oscar	132	/media/episodes/ep-11-01.webp	
1201	12	1	Tập 1: Cánh Cổng Shibuya	24	/media/episodes/ep-12-01.webp	
1401	14	1	Bản Đầy Đủ	138	/media/episodes/ep-14-01.webp	
1501	15	1	Bản Chiếu Rạp	105	/media/episodes/ep-15-01.webp	
1601	16	1	Bản Điện Ảnh	134	/media/episodes/ep-16-01.webp	
1701	17	1	Bản Rạp Full HD	134	/media/episodes/ep-17-01.webp	
1801	18	1	Tập 1: Bánh Mì Và Xổ Số	55	/media/episodes/ep-18-01.webp	
1901	19	1	Tập 1: Khủng Hoảng Hôn Nhân	75	/media/episodes/ep-19-01.webp	
2001	20	1	Bản Chuẩn Chiếu Rạp	118	/media/episodes/ep-20-01.webp	
2101	21	1	Bản Chiếu Rạp 4K	169	/media/episodes/ep-21-01.webp	
2201	22	1	Bản Chuẩn Hoạt Hình 4K	140	/media/episodes/ep-22-01.webp	
2301	23	1	Bản Chiếu Rạp IMAX 4K	130	/media/episodes/ep-23-01.webp	
2401	24	1	Bản Chiếu Rạp 4K UHD	192	/media/episodes/ep-24-01.webp	
2501	25	1	Tập 1: Khi Bạn Lạc Trong Bóng Tối	81	/media/episodes/ep-25-01.webp	
2601	26	1	Tập 1: Khởi Đầu Của Heisenberg	58	/media/episodes/ep-26-01.webp	
3001	30	1	Bản Chiếu Rạp IMAX	148	/media/episodes/ep-30-01.webp	
3101	31	1	Tập 1: Để Đánh Bại Kibutsuji Muzan	48	/media/episodes/ep-31-01.webp	
3201	32	1	Tập 1: Tiếng Gầm Của Tự Do	61	/media/episodes/ep-32-01.webp	
3301	33	1	Tập 1: Kết Thúc Của Cuộc Phiêu Lưu	24	/media/episodes/ep-33-01.webp	
3601	36	1	Bản Đoạt Giải Oscar	125	/media/episodes/ep-36-01.webp	
3701	37	1	Tập 1: Tín Hiệu Từ Phobos	47	/media/episodes/ep-37-01.webp	Diễn biến tập 1 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3702	37	2	Tập 2: Khoang Số Bảy	48	/media/episodes/ep-37-02.webp	Diễn biến tập 2 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3703	37	3	Tập 3: Bản Sao Trong Gương	49	/media/episodes/ep-37-03.webp	Diễn biến tập 3 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3704	37	4	Tập 4: Mười Bảy Phút Mất Tích	47	/media/episodes/ep-37-04.webp	Diễn biến tập 4 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3705	37	5	Tập 5: Vùng Tối Quỹ Đạo	48	/media/episodes/ep-37-05.webp	Diễn biến tập 5 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3706	37	6	Tập 6: Ký Ức Của Người Chết	49	/media/episodes/ep-37-06.webp	Diễn biến tập 6 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3707	37	7	Tập 7: Cửa Thoát Cuối Cùng	47	/media/episodes/ep-37-07.webp	Diễn biến tập 7 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3708	37	8	Tập 8: Bình Minh Trên Sao Hỏa	48	/media/episodes/ep-37-08.webp	Diễn biến tập 8 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3801	38	1	Tập 1: Bản Điện Ảnh	123	/media/episodes/ep-38-01.webp	Diễn biến tập 1 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3901	39	1	Tập 1: Đứa Trẻ Của Tầng Mây	24	/media/episodes/ep-39-01.webp	Diễn biến tập 1 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3902	39	2	Tập 2: Cổ Vật Thức Giấc	25	/media/episodes/ep-39-02.webp	Diễn biến tập 2 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3903	39	3	Tập 3: Bản Đồ Không Có Phương Bắc	26	/media/episodes/ep-39-03.webp	Diễn biến tập 3 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3904	39	4	Tập 4: Thành Phố Cánh Buồm	24	/media/episodes/ep-39-04.webp	Diễn biến tập 4 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3905	39	5	Tập 5: Kẻ Săn Bão	25	/media/episodes/ep-39-05.webp	Diễn biến tập 5 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3906	39	6	Tập 6: Đêm Trên Đảo Trôi	26	/media/episodes/ep-39-06.webp	Diễn biến tập 6 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3907	39	7	Tập 7: Trái Tim Của Cỗ Máy	24	/media/episodes/ep-39-07.webp	Diễn biến tập 7 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3908	39	8	Tập 8: Cuộc Vây Hãm Trên Không	25	/media/episodes/ep-39-08.webp	Diễn biến tập 8 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3909	39	9	Tập 9: Bầu Trời Vỡ Đôi	26	/media/episodes/ep-39-09.webp	Diễn biến tập 9 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
3910	39	10	Tập 10: Ngày Gió Trở Về	24	/media/episodes/ep-39-10.webp	Diễn biến tập 10 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4001	40	1	Tập 1: Chuyến Tàu Trở Về	61	/media/episodes/ep-40-01.webp	Diễn biến tập 1 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4002	40	2	Tập 2: Ngôi Nhà Có Ánh Đèn	62	/media/episodes/ep-40-02.webp	Diễn biến tập 2 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4003	40	3	Tập 3: Bức Thư Chưa Gửi	63	/media/episodes/ep-40-03.webp	Diễn biến tập 3 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4004	40	4	Tập 4: Con Đường Ven Biển	61	/media/episodes/ep-40-04.webp	Diễn biến tập 4 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4005	40	5	Tập 5: Ngày Tuyết Đầu Mùa	62	/media/episodes/ep-40-05.webp	Diễn biến tập 5 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4006	40	6	Tập 6: Lời Hứa Ở Sân Ga	63	/media/episodes/ep-40-06.webp	Diễn biến tập 6 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4007	40	7	Tập 7: Khoảng Cách Một Thành Phố	61	/media/episodes/ep-40-07.webp	Diễn biến tập 7 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4008	40	8	Tập 8: Căn Phòng Số 203	62	/media/episodes/ep-40-08.webp	Diễn biến tập 8 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4009	40	9	Tập 9: Sau Cơn Bão Tuyết	63	/media/episodes/ep-40-09.webp	Diễn biến tập 9 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4010	40	10	Tập 10: Điều Chúng Ta Bỏ Lỡ	61	/media/episodes/ep-40-10.webp	Diễn biến tập 10 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4011	40	11	Tập 11: Chuyến Tàu Cuối	62	/media/episodes/ep-40-11.webp	Diễn biến tập 11 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
4012	40	12	Tập 12: Mùa Xuân Đầu Tiên	63	/media/episodes/ep-40-12.webp	Diễn biến tập 12 của bộ phim, tiếp nối câu chuyện với những khám phá và xung đột mới.
\.


--
-- Data for Name: genres; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.genres ("Id", "Name", "Slug") FROM stdin;
1	Hành Động	hanh-dong
2	Khoa Học Viễn Tưởng	khoa-hoc-vien-tuong
3	Kỳ Ảo	ky-ao
4	Phiêu Lưu	phieu-luu
5	Kịch Tính	kich-tinh
6	Anime	anime
7	Lịch Sử	lich-su
8	Tội Phạm	toi-pham
9	Tâm Lý	tam-ly
10	Hài Hước	hai-huoc
11	Kinh Dị	kinh-di
12	Lãng Mạn	lang-man
13	Gia Đình	gia-dinh
14	Hoạt Hình	hoat-hinh
15	Chiến Tranh	chien-tranh
16	C??ng Ngh???	cong-nghe
17	??m Nh???c	am-nhac
\.


--
-- Data for Name: mfa_challenges; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.mfa_challenges ("TokenHash", "UserId", "ExpiresAtUtc", "FailedAttempts", "Version") FROM stdin;
\.


--
-- Data for Name: mfa_credentials; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.mfa_credentials ("UserId", "ProtectedSecret", "RecoveryCodeHashes", "Enabled", "EnabledAtUtc", "LastUsedTimeStep", "Version") FROM stdin;
\.


--
-- Data for Name: mfa_verified_sessions; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.mfa_verified_sessions ("TokenHash", "UserId", "VerifiedAtUtc") FROM stdin;
\.


--
-- Data for Name: movie_genres; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.movie_genres ("MovieId", "GenreId") FROM stdin;
1	1
1	2
1	4
1	5
2	5
2	7
2	9
3	1
3	2
3	3
3	14
4	1
4	5
4	8
5	1
5	3
5	4
5	6
6	5
6	7
6	15
7	2
7	4
7	5
8	2
8	3
8	11
9	1
9	2
9	5
10	1
10	3
10	5
11	5
11	9
11	10
12	1
12	3
12	6
13	5
13	9
13	12
14	5
14	9
14	13
15	9
15	11
16	4
16	5
16	7
17	3
17	5
17	11
18	1
18	5
18	8
19	5
19	10
19	12
20	1
20	5
20	11
21	1
21	5
21	8
22	1
22	2
22	4
22	14
23	1
23	4
23	5
24	2
24	3
24	4
25	1
25	2
25	5
25	11
26	5
26	8
26	9
27	1
27	5
27	8
28	9
28	10
28	12
29	1
29	5
29	8
30	1
30	3
30	4
30	7
31	1
31	3
31	6
32	1
32	3
32	5
32	6
33	3
33	4
33	5
33	6
34	1
34	3
34	6
34	11
35	3
35	6
35	12
35	14
36	3
36	4
36	6
36	14
37	2
37	4
37	5
38	5
38	8
38	9
39	1
39	3
39	4
39	6
40	5
40	9
40	12
41	16
41	2
42	1
42	4
43	1
43	5
44	1
44	2
44	5
45	17
\.


--
-- Data for Name: movies; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.movies ("Id", "Title", "OriginalTitle", "Slug", "Overview", "PosterUrl", "BackdropUrl", "TrailerUrl", "Rating", "VoteCount", "ReleaseYear", "DurationMinutes", "AgeRating", "Quality", "Type", "Country", "ViewCount", "IsTrending", "IsFeatured", "Director", "SearchText", "TotalEpisodes", "CurrentEpisode", search_vector) FROM stdin;
41	Hướng Dẫn Cài Giao Diện Sạch Tivi Android	Clean UI & Debloat Android TV - No Laptop AI	huong-dan-cai-giao-dien-sach-tivi-android	Video hướng dẫn thực tế cài giao diện sạch (Clean UI), xoá ứng dụng rác cho Android TV cực dễ dàng mà không cần dùng đến laptop hay AI. Tối ưu bộ nhớ, loại bỏ quảng cáo và giúp tivi hoạt động mượt mà.	/images/movies/movie-1-poster.webp	/images/movies/movie-1-backdrop.webp	https://www.youtube.com/watch?v=Way9Dexny3w	9.8	128500	2026	25	All	1080p FHD	1	Vi???t Nam	2500000	t	t	K??? Nh??	Huong Dan Cai Giao Dien Sach Xoa App Rac Cho Tivi Android Cuc De Khong Can Laptop AI Ky Nhu	1	1	'ai':18,36,65 'android':12,28,32,53 'app':8 'bỏ':71 'bộ':68 'cai':3 'can':16 'cho':10,52 'clean':29,46 'cuc':13 'cài':23,42 'cáo':73 'cần':60 'cực':55 'dan':2 'de':14 'debloat':31 'dien':5 'diện':25,44 'dàng':57 'dùng':61 'dẫn':22,39 'dễ':56 'dụng':50 'giao':4,24,43 'giúp':75 'hay':64 'hoạt':77 'huong':1 'hướng':21,38 'khong':15 'không':59 'ky':19 'laptop':17,35,63 'loại':70 'mà':58,80 'mượt':79 'nhu':20 'nhớ':69 'no':34 'quảng':72 'rac':9 'rác':51 'sach':6 'sạch':26,45 'thực':40 'tivi':11,27,76 'tv':33,54 'tế':41 'tối':66 'ui':30,47 'video':37 'và':74 'xoa':7 'xoá':48 'đến':62 'động':78 'ưu':67 'ứng':49
43	Tuyển Tập Video MP4 Đặc Sắc	Featured Video Short (Video.mp4)	tuyen-tap-video-mp4-dac-sac	Trích đoạn clip ngắn chất lượng cao từ kho lưu trữ đám mây cá nhân Cloudflare R2.	/images/movies/movie-3-poster.webp	/images/movies/movie-3-backdrop.webp	https://www.youtube.com/watch?v=Way9Dexny3w	9.2	64000	2026	5	All	1080p FHD	1	Vi???t Nam	1200000	t	f	CinemaFlow Studio	Tuyen Tap Video MP4 Dac Sac Short Video R2	1	1	'cao':26 'chất':24 'clip':22 'cloudflare':35 'cá':33 'dac':5 'featured':16 'kho':28 'lưu':29 'lượng':25 'mp4':4,13 'mây':32 'ngắn':23 'nhân':34 'r2':9,36 'sac':6 'short':7,18 'sắc':15 'tap':2 'trích':20 'trữ':30 'tuyen':1 'tuyển':10 'tập':11 'từ':27 'video':3,8,12,17 'video.mp4':19 'đoạn':21 'đám':31 'đặc':14
8	Cậu Bé Mất Tích (Stranger Things Mùa 5)	Stranger Things Season 5	stranger-things-season-5	Mùa cuối cùng khép lại cuộc chiến chống lại Vecna và Thế Giới Ngược (Upside Down) đe dọa nuốt chửng thị trấn Hawkins và cả thế giới.	/images/movies/movie-8-poster.webp	/images/movies/movie-8-backdrop.webp	https://www.youtube.com/watch?v=b9EkMc79ZSU	8.8	310000	2025	65	16+	4K Dolby Vision	2	Âu Mỹ	4800000	t	f	The Duffer Brothers	cau be mat tich (stranger things mua 5) stranger things season 5 the duffer brothers millie bobby brown finn wolfhard winona ryder david harbour mua cuoi cung khep lai cuoc chien chong lai vecna va the gioi nguoc (upside down) de doa nuot chung thi tran hawkins va ca the gioi.	8	8	'5':8A,12B 'be':2A 'bobby':17 'brothers':15C 'brown':18 'ca':49 'cau':1A 'chien':31 'chong':32 'chung':44 'cung':27 'cuoc':30 'cuoi':26 'david':23 'de':41 'doa':42 'down':40 'duffer':14C 'finn':19 'gioi':37,51 'harbour':24 'hawkins':47 'khep':28 'lai':29,33 'mat':3A 'millie':16 'mua':7A,25 'nguoc':38 'nuot':43 'ryder':22 'season':11B 'stranger':5A,9B 'the':13C,36,50 'thi':45 'things':6A,10B 'tich':4A 'tran':46 'upside':39 'va':35,48 'vecna':34 'winona':21 'wolfhard':20
12	Chú Thuật Hồi Chiến: Biến Cố Shibuya	Jujutsu Kaisen: Shibuya Incident	jujutsu-kaisen-shibuya	Vào đêm Halloween tại Shibuya, nhóm Chú Nguyền Sư do Kenjaku và Mahito cầm đầu thực hiện âm mưu phong ấn Chú Thuật Sư mạnh nhất: Gojo Satoru.	/images/movies/movie-12-poster.webp	/images/movies/movie-12-backdrop.webp	https://www.youtube.com/watch?v=9OhV3IWUsxE	8.9	220000	2023	24	16+	Full HD 1080p	3	Nhật Bản	3850000	t	f	Sunghoo Park, Shota Goshozono	chu thuat hoi chien: bien co shibuya jujutsu kaisen: shibuya incident sunghoo park, shota goshozono junya enoki yuma uchida asami seto yuichi nakamura vao dem halloween tai shibuya, nhom chu nguyen su do kenjaku va mahito cam dau thuc hien am muu phong an chu thuat su manh nhat: gojo satoru.	23	23	'am':41 'an':44 'asami':20 'bien':5A 'cam':37 'chien':4A 'chu':1A,30,45 'co':6A 'dau':38 'dem':25 'do':33 'enoki':17 'gojo':50 'goshozono':15C 'halloween':26 'hien':40 'hoi':3A 'incident':11B 'jujutsu':8B 'junya':16 'kaisen':9B 'kenjaku':34 'mahito':36 'manh':48 'muu':42 'nakamura':23 'nguyen':31 'nhat':49 'nhom':29 'park':13C 'phong':43 'satoru':51 'seto':21 'shibuya':7A,10B,28 'shota':14C 'su':32,47 'sunghoo':12C 'tai':27 'thuat':2A,46 'thuc':39 'uchida':19 'va':35 'vao':24 'yuichi':22 'yuma':18
32	Đại Chiến Titan: Hồi Kết Tuyệt Đối	Attack on Titan: The Final Chapters	attack-on-titan-final-chapters	Eren Yeager kích hoạt cuộc Rung Chấn hủy diệt thế giới, buộc những đồng đội cũ phải liên minh để đối đầu với anh trong trận chiến định đoạt số phận nhân loại.	/images/movies/movie-32-poster.webp	/images/movies/movie-32-backdrop.webp	https://www.youtube.com/watch?v=3xNH23QkNpk	9.2	380000	2023	85	18+	Full HD 1080p	3	Nhật Bản	6900000	t	f	Yuichiro Hayashi	dai chien titan: hoi ket tuyet doi attack on titan: the final chapters yuichiro hayashi yuki kaji yui ishikawa marina inoue hiroshi kamiya eren yeager kich hoat cuoc rung chan huy diet the gioi, buoc nhung dong doi cu phai lien minh de doi dau voi anh trong tran chien dinh doat so phan nhan loai.	2	2	'anh':47 'attack':8B 'buoc':35 'chan':30 'chapters':13B 'chien':2A,50 'cu':39 'cuoc':28 'dai':1A 'dau':45 'de':43 'diet':32 'dinh':51 'doat':52 'doi':7A,38,44 'dong':37 'eren':24 'final':12B 'gioi':34 'hayashi':15C 'hiroshi':22 'hoat':27 'hoi':4A 'huy':31 'inoue':21 'ishikawa':19 'kaji':17 'kamiya':23 'ket':5A 'kich':26 'lien':41 'loai':56 'marina':20 'minh':42 'nhan':55 'nhung':36 'on':9B 'phai':40 'phan':54 'rung':29 'so':53 'the':11B,33 'titan':3A,10B 'tran':49 'trong':48 'tuyet':6A 'voi':46 'yeager':25 'yui':18 'yuichiro':14C 'yuki':16
42	Xu Hướng Việc Làm Hà Nam 2026	Trending TikTok Jobs Ha Nam 2026	xu-huong-viec-lam-ha-nam-2026	Tổng hợp clip xu hướng TikTok nổi bật năm 2026 về tìm kiếm việc làm, giới thiệu việc làm và các cơ hội nghề nghiệp tại Hà Nam.	/images/movies/movie-2-poster.webp	/images/movies/movie-2-backdrop.webp	https://www.youtube.com/watch?v=Way9Dexny3w	9.5	89000	2026	15	All	1080p FHD	1	Vi???t Nam	1800000	t	t	TikTok Creator	Xu Huong Viec Lam Ha Nam TikTok 2026	1	1	'2026':8,15,21,31 'bật':29 'clip':24 'các':42 'cơ':43 'giới':37 'ha':5,19 'huong':2 'hà':13,48 'hướng':10,26 'hội':44 'hợp':23 'jobs':18 'kiếm':34 'lam':4 'làm':12,36,40 'nam':6,14,20,49 'nghiệp':46 'nghề':45 'năm':30 'nổi':28 'thiệu':38 'tiktok':7,17,27 'trending':16 'tìm':33 'tại':47 'tổng':22 'viec':3 'việc':11,35,39 'và':41 'về':32 'xu':1,9,25
35	Tên Cậu Là Gì? (Your Name)	Kimi no Na wa.	your-name	Hai thiếu niên xa lạ sống ở Tokyo và vùng nông thôn hẻo lánh bỗng hoán đổi thân xác trong giấc mơ, cùng chạy đua với thời gian để ngăn thảm họa sao băng.	/images/movies/movie-35-poster.webp	/images/movies/movie-35-backdrop.webp	https://www.youtube.com/watch?v=xU47nhruN-Q	8.9	385000	2016	106	13+	4K Remastered	3	Nhật Bản	6400000	f	f	Makoto Shinkai	ten cau la gi? (your name) kimi no na wa. makoto shinkai ryunosuke kamiki mone kamishiraishi ryo narita hai thieu nien xa la song o tokyo va vung nong thon heo lanh bong hoan doi than xac trong giac mo, cung chay dua voi thoi gian de ngan tham hoa sao bang.	1	1	'bang':52 'bong':33 'cau':2A 'chay':42 'cung':41 'de':47 'doi':35 'dua':43 'gi':4A 'giac':39 'gian':46 'hai':19 'heo':31 'hoa':50 'hoan':34 'kamiki':14 'kamishiraishi':16 'kimi':7B 'la':3A,23 'lanh':32 'makoto':11C 'mo':40 'mone':15 'na':9B 'name':6A 'narita':18 'ngan':48 'nien':21 'no':8B 'nong':29 'o':25 'ryo':17 'ryunosuke':13 'sao':51 'shinkai':12C 'song':24 'ten':1A 'tham':49 'than':36 'thieu':20 'thoi':45 'thon':30 'tokyo':26 'trong':38 'va':27 'voi':44 'vung':28 'wa':10B 'xa':22 'xac':37 'your':5A
19	Nữ Hoàng Nước Mắt (Queen of Tears)	Queen of Tears	queen-of-tears	Cuộc hôn nhân khủng hoảng và sự hồi sinh diệu kỳ trong tình yêu giữa nữ tài phiệt kiêu kỳ và chàng luật sư tỉnh lẻ tài năng.	/images/movies/movie-19-poster.webp	/images/movies/movie-19-backdrop.webp	https://www.youtube.com/watch?v=Gg2D8zrzlOA	8.6	210000	2024	75	16+	Full HD 1080p	2	Hàn Quốc	5100000	t	f	Jang Young-woo, Kim Hee-won	nu hoang nuoc mat (queen of tears) queen of tears jang young-woo, kim hee-won kim soo-hyun kim ji-won park sung-hoon kwak dong-yeon cuoc hon nhan khung hoang va su hoi sinh dieu ky trong tinh yeu giua nu tai phiet kieu ky va chang luat su tinh le tai nang.	16	16	'chang':56 'cuoc':35 'dieu':44 'dong':33 'dong-yeon':32 'giua':49 'hee':17C 'hee-won':16C 'hoang':2A,39 'hoi':42 'hon':36 'hoon':30 'hyun':22 'jang':11C 'ji':25 'ji-won':24 'khung':38 'kieu':53 'kim':15C,19,23 'kwak':31 'ky':45,54 'le':60 'luat':57 'mat':4A 'nang':62 'nhan':37 'nu':1A,50 'nuoc':3A 'of':6A,9B 'park':27 'phiet':52 'queen':5A,8B 'sinh':43 'soo':21 'soo-hyun':20 'su':41,58 'sung':29 'sung-hoon':28 'tai':51,61 'tears':7A,10B 'tinh':47,59 'trong':46 'va':40,55 'won':18C,26 'woo':14C 'yeon':34 'yeu':48 'young':13C 'young-woo':12C
31	Thanh Gươm Diệt Quỷ: Đại Trụ Huấn Luyện	Demon Slayer: Kimetsu no Yaiba - Hashira Training Arc	kimetsu-no-yaiba-hashira-training	Tanjiro cùng Sát Quỷ Đoàn tham gia khóa huấn luyện khắt khe cùng các Đại Trụ để chuẩn bị cho trận đại chiến cuối cùng chống lại Chúa Quỷ Muzan.	/images/movies/movie-31-poster.webp	/images/movies/movie-31-backdrop.webp	https://www.youtube.com/watch?v=a9tq0aS5Zu8	8.9	265000	2024	24	16+	Full HD 1080p	3	Nhật Bản	5800000	t	f	Haruo Sotozaki	thanh guom diet quy: dai tru huan luyen demon slayer: kimetsu no yaiba - hashira training arc haruo sotozaki natsuki hanae akari kito hiro shimono yoshitsugu matsuoka tanjiro cung sat quy doan tham gia khoa huan luyen khat khe cung cac dai tru de chuan bi cho tran dai chien cuoi cung chong lai chua quy muzan.	8	8	'akari':21 'arc':16B 'bi':45 'cac':40 'chien':49 'cho':46 'chong':52 'chua':54 'chuan':44 'cung':28,39,51 'cuoi':50 'dai':5A,41,48 'de':43 'demon':9B 'diet':3A 'doan':31 'gia':33 'guom':2A 'hanae':20 'haruo':17C 'hashira':14B 'hiro':23 'huan':7A,35 'khat':37 'khe':38 'khoa':34 'kimetsu':11B 'kito':22 'lai':53 'luyen':8A,36 'matsuoka':26 'muzan':56 'natsuki':19 'no':12B 'quy':4A,30,55 'sat':29 'shimono':24 'slayer':10B 'sotozaki':18C 'tanjiro':27 'tham':32 'thanh':1A 'training':15B 'tran':47 'tru':6A,42 'yaiba':13B 'yoshitsugu':25
6	Shogun: Tướng Quân	Shōgun	shogun	Tại Nhật Bản thời phong kiến năm 1600, Lãnh chúa Yoshii Toranaga chiến đấu bảo vệ tính mạng khi các kẻ thù trong Hội đồng Nhiếp chính hợp lực chống lại ông.	/images/movies/movie-6-poster.webp	/images/movies/movie-6-backdrop.webp	https://www.youtube.com/watch?v=yAN5uspO_hk	8.8	175000	2024	60	18+	4K UHD HDR	2	Âu Mỹ	2950000	t	f	Rachel Kondo, Justin Marks	shogun: tuong quan shogun rachel kondo, justin marks hiroyuki sanada cosmo jarvis anna sawai tadanobu asano tai nhat ban thoi phong kien nam 1600, lanh chua yoshii toranaga chien dau bao ve tinh mang khi cac ke thu trong hoi dong nhiep chinh hop luc chong lai ong.	10	10	'1600':24 'anna':13 'asano':16 'ban':19 'bao':31 'cac':36 'chien':29 'chinh':43 'chong':46 'chua':26 'cosmo':11 'dau':30 'dong':41 'hiroyuki':9 'hoi':40 'hop':44 'jarvis':12 'justin':7C 'ke':37 'khi':35 'kien':22 'kondo':6C 'lai':47 'lanh':25 'luc':45 'mang':34 'marks':8C 'nam':23 'nhat':18 'nhiep':42 'ong':48 'phong':21 'quan':3A 'rachel':5C 'sanada':10 'sawai':14 'shogun':1A,4B 'tadanobu':15 'tai':17 'thoi':20 'thu':38 'tinh':33 'toranaga':28 'trong':39 'tuong':2A 've':32 'yoshii':27
7	Hố Đen Tử Thần	Interstellar	interstellar	Khi Trái Đất bên bờ vực diệt vong, một nhóm nhà thám hiểm du hành qua lỗ sâu không gian để tìm kiếm ngôi nhà mới cho nhân loại.	/images/movies/movie-7-poster.webp	/images/movies/movie-7-backdrop.webp	https://www.youtube.com/watch?v=zSWdZVtXT7E	8.7	380000	2014	169	13+	4K Remastered	1	Âu Mỹ	6100000	f	f	Christopher Nolan	ho den tu than interstellar christopher nolan matthew mcconaughey anne hathaway jessica chastain michael caine khi trai dat ben bo vuc diet vong, mot nhom nha tham hiem du hanh qua lo sau khong gian de tim kiem ngoi nha moi cho nhan loai.	1	1	'anne':10 'ben':19 'bo':20 'caine':15 'chastain':13 'cho':42 'christopher':6C 'dat':18 'de':36 'den':2A 'diet':22 'du':29 'gian':35 'hanh':30 'hathaway':11 'hiem':28 'ho':1A 'interstellar':5B 'jessica':12 'khi':16 'khong':34 'kiem':38 'lo':32 'loai':44 'matthew':8 'mcconaughey':9 'michael':14 'moi':41 'mot':24 'ngoi':39 'nha':26,40 'nhan':43 'nhom':25 'nolan':7C 'qua':31 'sau':33 'tham':27 'than':4A 'tim':37 'trai':17 'tu':3A 'vong':23 'vuc':21
45	Meyo - Half Asleep (Lyrics Official)	Meyo - Half Asleep (Lyrics)	meyo-half-asleep-lyrics	Tác phẩm âm nhạc Meyo - Half Asleep với giai điệu sâu lắng và lời bài hát đầy cảm xúc.	/images/movies/movie-5-poster.webp	/images/movies/movie-5-backdrop.webp	https://www.youtube.com/watch?v=Way9Dexny3w	9.4	48000	2026	4	All	Lossless Audio	1	Qu???c T???	890000	f	f	Meyo	Meyo Half Asleep Lyrics Audio Music	1	1	'asleep':3,9,14,22 'audio':5 'bài':30 'cảm':33 'giai':24 'half':2,8,13,21 'hát':31 'lyrics':4,10,15 'lắng':27 'lời':29 'meyo':1,7,12,20 'music':6 'nhạc':19 'official':11 'phẩm':17 'sâu':26 'tác':16 'và':28 'với':23 'xúc':34 'âm':18 'điệu':25 'đầy':32
44	Điện Ảnh Chọn Lọc (Play MP4)	Curated Cinema Feature (play.mp4)	dien-anh-chon-loc-play-mp4	Bản phim chiếu rạp chuẩn điện ảnh 72.8 MB được lưu trữ trực tiếp trên bucket cá nhân Cloudflare R2 tốc độ cao.	/images/movies/movie-4-poster.webp	/images/movies/movie-4-backdrop.webp	https://www.youtube.com/watch?v=Way9Dexny3w	9.7	195000	2026	45	16+	4K Ultra HD	1	??u M???	3400000	t	t	CinemaFlow Master	Dien Anh Chon Loc Play MP4 4K Cloudflare R2	1	1	'4k':7 '72.8':27 'anh':2 'bucket':35 'bản':20 'cao':42 'chiếu':22 'chon':3 'chuẩn':24 'chọn':12 'cinema':17 'cloudflare':8,38 'curated':16 'cá':36 'dien':1 'feature':18 'loc':4 'lưu':30 'lọc':13 'mb':28 'mp4':6,15 'nhân':37 'phim':21 'play':5,14 'play.mp4':19 'r2':9,39 'rạp':23 'tiếp':33 'trên':34 'trữ':31 'trực':32 'tốc':40 'điện':10,25 'được':29 'độ':41 'ảnh':11,26
38	Sài Gòn Sau Nửa Đêm	Saigon After Midnight	sai-gon-sau-nua-dem	Một nữ phóng viên ảnh lần theo cuộn phim thất lạc và bị cuốn vào mạng lưới bí mật trải dài qua những con hẻm Sài Gòn. Mỗi bức ảnh hé lộ thêm một phần của vụ án đã bị lãng quên.	/images/movies/movie-38-poster.webp	/images/movies/movie-38-backdrop.webp	https://www.youtube.com/watch?v=SXiWz0yLLdE	8.4	52780	2025	124	18+	4K UHD	1	Việt Nam	932000	t	f	Lê Minh Khôi	sai gon sau nua dem saigon after midnight le minh khoi tran lam nguyen hoang phuc bao anh khanh van mot nu phong vien anh lan theo cuon phim that lac va bi cuon vao mang luoi bi mat trai dai qua nhung con hem sai gon. moi buc anh he lo them mot phan cua vu an da bi lang quen.	1	1	'after':7B 'an':58 'anh':18,25,50 'bao':17 'bi':33,38,60 'buc':49 'con':44 'cua':56 'cuon':28,34 'da':59 'dai':41 'dem':5A 'gon':2A,47 'he':51 'hem':45 'hoang':15 'khanh':19 'khoi':11C 'lac':31 'lam':13 'lan':26 'lang':61 'le':9C 'lo':52 'luoi':37 'mang':36 'mat':39 'midnight':8B 'minh':10C 'moi':48 'mot':21,54 'nguyen':14 'nhung':43 'nu':22 'nua':4A 'phan':55 'phim':29 'phong':23 'phuc':16 'qua':42 'quen':62 'sai':1A,46 'saigon':6B 'sau':3A 'that':30 'them':53 'theo':27 'trai':40 'tran':12 'va':32 'van':20 'vao':35 'vien':24 'vu':57
40	Mùa Tuyết Cuối Cùng	The Last Snowfall	mua-tuyet-cuoi-cung	Một kiến trúc sư trở về thị trấn ven biển để khôi phục nhà ga cũ và gặp lại người bạn từng rời đi không lời từ biệt. Mùa đông cuối cùng của thị trấn buộc họ đối diện với những điều chưa từng nói.	/images/movies/movie-40-poster.webp	/images/movies/movie-40-backdrop.webp	https://www.youtube.com/watch?v=RYmQHMC8NII	8.6	74360	2025	62	13+	4K HDR	2	Hàn Quốc	1475000	f	f	Han Seo-jun	mua tuyet cuoi cung the last snowfall han seo-jun kim ji-won lee do-hyun park min-young jung hae-in mot kien truc su tro ve thi tran ven bien de khoi phuc nha ga cu va gap lai nguoi ban tung roi di khong loi tu biet. mua dong cuoi cung cua thi tran buoc ho doi dien voi nhung dieu chua tung noi.	12	12	'ban':48 'bien':37 'biet':55 'buoc':63 'chua':70 'cu':43 'cua':60 'cung':4A,59 'cuoi':3A,58 'de':38 'di':51 'dien':66 'dieu':69 'do':18 'do-hyun':17 'doi':65 'dong':57 'ga':42 'gap':45 'hae':26 'hae-in':25 'han':8C 'ho':64 'hyun':19 'in':27 'ji':14 'ji-won':13 'jun':11C 'jung':24 'khoi':39 'khong':52 'kien':29 'kim':12 'lai':46 'last':6B 'lee':16 'loi':53 'min':22 'min-young':21 'mot':28 'mua':1A,56 'nguoi':47 'nha':41 'nhung':68 'noi':72 'park':20 'phuc':40 'roi':50 'seo':10C 'seo-jun':9C 'snowfall':7B 'su':31 'the':5B 'thi':34,61 'tran':35,62 'tro':32 'truc':30 'tu':54 'tung':49,71 'tuyet':2A 'va':44 've':33 'ven':36 'voi':67 'won':15 'young':23
26	Biệt Đội Cảm Tử: Biến Chất (Breaking Bad)	Breaking Bad	breaking-bad	Một giáo viên hóa học trung học được chẩn đoán mắc bệnh ung thư giai đoạn cuối đã hợp tác với cựu học sinh để chế tạo ma túy đá nhằm bảo đảm tương lai cho gia đình.	/images/movies/movie-26-poster.webp	/images/movies/movie-26-backdrop.webp	https://www.youtube.com/watch?v=HhesaQXLuRY	9.5	540000	2013	48	18+	4K Remastered	2	Âu Mỹ	7200000	f	f	Vince Gilligan	biet doi cam tu: bien chat (breaking bad) breaking bad vince gilligan bryan cranston aaron paul anna gunn bob odenkirk mot giao vien hoa hoc trung hoc duoc chan doan mac benh ung thu giai doan cuoi da hop tac voi cuu hoc sinh de che tao ma tuy da nham bao dam tuong lai cho gia dinh.	62	62	'aaron':15 'anna':17 'bad':8A,10B 'bao':52 'benh':32 'bien':5A 'biet':1A 'bob':19 'breaking':7A,9B 'bryan':13 'cam':3A 'chan':29 'chat':6A 'che':46 'cho':56 'cranston':14 'cuoi':37 'cuu':42 'da':38,50 'dam':53 'de':45 'dinh':58 'doan':30,36 'doi':2A 'duoc':28 'gia':57 'giai':35 'giao':22 'gilligan':12C 'gunn':18 'hoa':24 'hoc':25,27,43 'hop':39 'lai':55 'ma':48 'mac':31 'mot':21 'nham':51 'odenkirk':20 'paul':16 'sinh':44 'tac':40 'tao':47 'thu':34 'trung':26 'tu':4A 'tuong':54 'tuy':49 'ung':33 'vien':23 'vince':11C 'voi':41
22	Người Nhện: Du Hành Vũ Trụ Nhện	Spider-Man: Across the Spider-Verse	spider-man-across-the-spider-verse	Miles Morales du hành qua đa vũ trụ nơi cậu gặp gỡ một đội ngũ Người Nhện ưu tú nhưng phải định nghĩa lại ý nghĩa của việc trở thành anh hùng.	/images/movies/movie-22-poster.webp	/images/movies/movie-22-backdrop.webp	https://www.youtube.com/watch?v=cqGjhVJWtEg	8.9	390000	2023	140	13+	4K Dolby Vision	1	Âu Mỹ	5300000	t	f	Joaquim Dos Santos, Kemp Powers	nguoi nhen: du hanh vu tru nhen spider-man: across the spider-verse joaquim dos santos, kemp powers shameik moore hailee steinfeld oscar isaac daniel kaluuya miles morales du hanh qua da vu tru noi cau gap go mot doi ngu nguoi nhen uu tu nhung phai dinh nghia lai y nghia cua viec tro thanh anh hung.	1	1	'across':11B 'anh':59 'cau':38 'cua':55 'da':34 'daniel':27 'dinh':50 'doi':42 'dos':17C 'du':3A,31 'gap':39 'go':40 'hailee':23 'hanh':4A,32 'hung':60 'isaac':26 'joaquim':16C 'kaluuya':28 'kemp':19C 'lai':52 'man':10B 'miles':29 'moore':22 'morales':30 'mot':41 'nghia':51,54 'ngu':43 'nguoi':1A,44 'nhen':2A,7A,45 'nhung':48 'noi':37 'oscar':25 'phai':49 'powers':20C 'qua':33 'santos':18C 'shameik':21 'spider':9B,14B 'spider-man':8B 'spider-verse':13B 'steinfeld':24 'thanh':58 'the':12B 'tro':57 'tru':6A,36 'tu':47 'uu':46 'verse':15B 'viec':56 'vu':5A,35 'y':53
18	Squid Game: Trò Chơi Con Mực (Mùa 2)	Squid Game Season 2	squid-game-season-2	Seong Gi-hun từ bỏ việc sang Mỹ, quyết tâm thâm nhập lại vào đấu trường trò chơi sinh tử bí ẩn để vạch trần tổ chức tàn bạo đứng sau.	/images/movies/movie-18-poster.webp	/images/movies/movie-18-backdrop.webp	https://www.youtube.com/watch?v=Ed1sGgHUo88	8.7	340000	2024	55	18+	4K Dolby Vision	2	Hàn Quốc	6200000	t	f	Hwang Dong-hyuk	squid game: tro choi con muc (mua 2) squid game season 2 hwang dong-hyuk lee jung-jae lee byung-hun wi ha-joon yim si-wan seong gi-hun tu bo viec sang my, quyet tam tham nhap lai vao dau truong tro choi sinh tu bi an de vach tran to chuc tan bao dung sau.	6	6	'2':8A,12B 'an':55 'bao':62 'bi':54 'bo':38 'byung':23 'byung-hun':22 'choi':4A,51 'chuc':60 'con':5A 'dau':48 'de':56 'dong':15C 'dong-hyuk':14C 'dung':63 'game':2A,10B 'gi':35 'gi-hun':34 'ha':27 'ha-joon':26 'hun':24,36 'hwang':13C 'hyuk':16C 'jae':20 'joon':28 'jung':19 'jung-jae':18 'lai':46 'lee':17,21 'mua':7A 'muc':6A 'my':41 'nhap':45 'quyet':42 'sang':40 'sau':64 'season':11B 'seong':33 'si':31 'si-wan':30 'sinh':52 'squid':1A,9B 'tam':43 'tan':61 'tham':44 'to':59 'tran':58 'tro':3A,50 'truong':49 'tu':37,53 'vach':57 'vao':47 'viec':39 'wan':32 'wi':25 'yim':29
36	Vùng Đất Linh Hồn (Spirited Away)	Sen to Chihiro no Kamikakushi	spirited-away	Cô bé 10 tuổi Chihiro lạc vào thế giới của các vị thần linh và phù thủy, phải dũng cảm làm việc tại nhà tắm hơi bí ẩn để giải cứu cha mẹ.	/images/movies/movie-36-poster.webp	/images/movies/movie-36-backdrop.webp	https://www.youtube.com/watch?v=ByXuk9QqQkk	9	450000	2001	125	G	Full HD Remaster	3	Nhật Bản	7800000	f	f	Hayao Miyazaki	vung dat linh hon (spirited away) sen to chihiro no kamikakushi hayao miyazaki rumi hiiragi miyu irino mari natsuki takashi naito co be 10 tuoi chihiro lac vao the gioi cua cac vi than linh va phu thuy, phai dung cam lam viec tai nha tam hoi bi an de giai cuu cha me.	1	1	'10':24 'an':49 'away':6A 'be':23 'bi':48 'cac':32 'cam':41 'cha':53 'chihiro':9B,26 'co':22 'cua':31 'cuu':52 'dat':2A 'de':50 'dung':40 'giai':51 'gioi':30 'hayao':12C 'hiiragi':15 'hoi':47 'hon':4A 'irino':17 'kamikakushi':11B 'lac':27 'lam':42 'linh':3A,35 'mari':18 'me':54 'miyazaki':13C 'miyu':16 'naito':21 'natsuki':19 'nha':45 'no':10B 'phai':39 'phu':37 'rumi':14 'sen':7B 'spirited':5A 'tai':44 'takashi':20 'tam':46 'than':34 'the':29 'thuy':38 'to':8B 'tuoi':25 'va':36 'vao':28 'vi':33 'viec':43 'vung':1A
20	Chuyến Tàu Sinh Tử	Train to Busan	train-to-busan	Khi dịch bệnh xác sống bùng phát dữ dội tại Hàn Quốc, các hành khách trên chuyến tàu cao tốc từ Seoul tới Busan phải chiến đấu sinh tử để sống sót.	/images/movies/movie-20-poster.webp	/images/movies/movie-20-backdrop.webp	https://www.youtube.com/watch?v=pyWuHv2-Abk	8.6	310000	2016	118	16+	Full HD 1080p	1	Hàn Quốc	4900000	f	f	Yeon Sang-ho	chuyen tau sinh tu train to busan yeon sang-ho gong yoo jung yu-mi ma dong-seok kim su-an khi dich benh xac song bung phat du doi tai han quoc, cac hanh khach tren chuyen tau cao toc tu seoul toi busan phai chien dau sinh tu de song sot.	1	1	'an':25 'benh':28 'bung':31 'busan':7B,49 'cac':38 'cao':44 'chien':51 'chuyen':1A,42 'dau':52 'de':55 'dich':27 'doi':34 'dong':20 'dong-seok':19 'du':33 'gong':12 'han':36 'hanh':39 'ho':11C 'jung':14 'khach':40 'khi':26 'kim':22 'ma':18 'mi':17 'phai':50 'phat':32 'quoc':37 'sang':10C 'sang-ho':9C 'seok':21 'seoul':47 'sinh':3A,53 'song':30,56 'sot':57 'su':24 'su-an':23 'tai':35 'tau':2A,43 'to':6B 'toc':45 'toi':48 'train':5B 'tren':41 'tu':4A,46,54 'xac':29 'yeon':8C 'yoo':13 'yu':16 'yu-mi':15
10	Gia Tộc Rồng (House of the Dragon Mùa 2)	House of the Dragon Season 2	house-of-the-dragon-season-2	Cuộc nội chiến 'Vũ Điệu Của Bầy Rồng' đẫm máu bùng nổ giữa hai phe Hắc Long (Rhaenyra) và Lục Long (Aegon II) để tranh giành Ngai Sắt.	/images/movies/movie-10-poster.webp	/images/movies/movie-10-backdrop.webp	https://www.youtube.com/watch?v=DotnJ7tTA34	8.5	210000	2024	62	18+	4K UHD	2	Âu Mỹ	3450000	t	f	Ryan Condal	gia toc rong (house of the dragon mua 2) house of the dragon season 2 ryan condal emma d'arcy matt smith olivia cooke rhys ifans cuoc noi chien 'vu dieu cua bay rong' dam mau bung no giua hai phe hac long (rhaenyra) va luc long (aegon ii) de tranh gianh ngai sat.	8	8	'2':9A,15B 'aegon':48 'arcy':20 'bay':33 'bung':37 'chien':29 'condal':17C 'cooke':24 'cua':32 'cuoc':27 'd':19 'dam':35 'de':50 'dieu':31 'dragon':7A,13B 'emma':18 'gia':1A 'gianh':52 'giua':39 'hac':42 'hai':40 'house':4A,10B 'ifans':26 'ii':49 'long':43,47 'luc':46 'matt':21 'mau':36 'mua':8A 'ngai':53 'no':38 'noi':28 'of':5A,11B 'olivia':23 'phe':41 'rhaenyra':44 'rhys':25 'rong':3A,34 'ryan':16C 'sat':54 'season':14B 'smith':22 'the':6A,12B 'toc':2A 'tranh':51 'va':45 'vu':30
2	Oppenheimer: Kẻ Hủy Diệt Thế Giới	Oppenheimer	oppenheimer	Câu chuyện về nhà vật lý lý thuyết J. Robert Oppenheimer, người lãnh đạo Dự án Manhattan phát triển vũ khí hạt nhân đầu tiên trong lịch sử nhân loại.	/images/movies/movie-2-poster.webp	/images/movies/movie-2-backdrop.webp	https://www.youtube.com/watch?v=uYPbbksJxIg	8.9	312000	2023	180	18+	4K IMAX	1	Âu Mỹ	2840000	t	f	Christopher Nolan	oppenheimer: ke huy diet the gioi oppenheimer christopher nolan cillian murphy emily blunt matt damon robert downey jr. cau chuyen ve nha vat ly ly thuyet j. robert oppenheimer, nguoi lanh dao du an manhattan phat trien vu khi hat nhan dau tien trong lich su nhan loai.	1	1	'an':34 'blunt':13 'cau':19 'christopher':8C 'chuyen':20 'cillian':10 'damon':15 'dao':32 'dau':42 'diet':4A 'downey':17 'du':33 'emily':12 'gioi':6A 'hat':40 'huy':3A 'j':27 'jr':18 'ke':2A 'khi':39 'lanh':31 'lich':45 'loai':48 'ly':24,25 'manhattan':35 'matt':14 'murphy':11 'nguoi':30 'nha':22 'nhan':41,47 'nolan':9C 'oppenheimer':1A,7B,29 'phat':36 'robert':16,28 'su':46 'the':5A 'thuyet':26 'tien':43 'trien':37 'trong':44 'vat':23 've':21 'vu':38
13	Mai	Mai	mai	Mai là một người phụ nữ ngoài ba mươi tuổi có quá khứ nhiều tổn thương, tìm kiếm tình yêu và sự chữa lành bên chàng trai trẻ Dương giữa định kiến xã hội.	/images/movies/movie-13-poster.webp	/images/movies/movie-13-backdrop.webp	https://www.youtube.com/watch?v=EX6clvId19s	8.2	145000	2024	131	18+	4K UHD	1	Việt Nam	4520000	t	f	Trấn Thành	mai mai tran thanh phuong anh dao tuan tran hong dao uyen an mai la mot nguoi phu nu ngoai ba muoi tuoi co qua khu nhieu ton thuong, tim kiem tinh yeu va su chua lanh ben chang trai tre duong giua dinh kien xa hoi.	1	1	'an':13 'anh':6 'ba':21 'ben':38 'chang':39 'chua':36 'co':24 'dao':7,11 'dinh':44 'duong':42 'giua':43 'hoi':47 'hong':10 'khu':26 'kiem':31 'kien':45 'la':15 'lanh':37 'mai':1A,2B,14 'mot':16 'muoi':22 'ngoai':20 'nguoi':17 'nhieu':27 'nu':19 'phu':18 'phuong':5 'qua':25 'su':35 'thanh':4C 'thuong':29 'tim':30 'tinh':32 'ton':28 'trai':40 'tran':3C,9 'tre':41 'tuan':8 'tuoi':23 'uyen':12 'va':34 'xa':46 'yeu':33
4	Kỵ Sĩ Bóng Đêm	The Dark Knight	the-dark-knight	Khi hiểm họa mang tên Joker xuất hiện tàn phá Gotham, Batman phải trải qua bài kiểm tra lớn nhất về tâm lý và thể xác để bảo vệ công lý.	/images/movies/movie-4-poster.webp	/images/movies/movie-4-backdrop.webp	https://www.youtube.com/watch?v=EXeTwQWrcwY	9	420000	2008	152	16+	4K Remastered	1	Âu Mỹ	5200000	t	f	Christopher Nolan	ky si bong dem the dark knight christopher nolan christian bale heath ledger aaron eckhart gary oldman khi hiem hoa mang ten joker xuat hien tan pha gotham, batman phai trai qua bai kiem tra lon nhat ve tam ly va the xac de bao ve cong ly.	1	1	'aaron':14 'bai':33 'bale':11 'bao':45 'batman':29 'bong':3A 'christian':10 'christopher':8C 'cong':47 'dark':6B 'de':44 'dem':4A 'eckhart':15 'gary':16 'gotham':28 'heath':12 'hiem':19 'hien':25 'hoa':20 'joker':23 'khi':18 'kiem':34 'knight':7B 'ky':1A 'ledger':13 'lon':36 'ly':40,48 'mang':21 'nhat':37 'nolan':9C 'oldman':17 'pha':27 'phai':30 'qua':32 'si':2A 'tam':39 'tan':26 'ten':22 'the':5B,42 'tra':35 'trai':31 'va':41 've':38,46 'xac':43 'xuat':24
27	Cuồng Phong	The Knockout	cuong-phong	Cuộc đấu trí kéo dài suốt 20 năm giữa cảnh sát hình sự chính trực An Hân và trùm thế lực ngầm Cao Khải Cường tại thành phố Lâm Hải.	/images/movies/movie-27-poster.webp	/images/movies/movie-27-backdrop.webp	https://www.youtube.com/watch?v=FicZ1DDQ1Zo	8.8	280000	2023	45	16+	Full HD 1080p	2	Trung Quốc	4300000	f	f	Từ Kỷ Chu	cuong phong the knockout tu ky chu truong dich truong tung van ly nhat dong truong chi kien cuoc dau tri keo dai suot 20 nam giua canh sat hinh su chinh truc an han va trum the luc ngam cao khai cuong tai thanh pho lam hai.	39	39	'20':25 'an':34 'canh':28 'cao':41 'chi':17 'chinh':32 'chu':7C 'cuoc':19 'cuong':1A,43 'dai':23 'dau':20 'dich':9 'dong':15 'giua':27 'hai':48 'han':35 'hinh':30 'keo':22 'khai':42 'kien':18 'knockout':4B 'ky':6C 'lam':47 'luc':39 'ly':13 'nam':26 'ngam':40 'nhat':14 'pho':46 'phong':2A 'sat':29 'su':31 'suot':24 'tai':44 'thanh':45 'the':3B,38 'tri':21 'truc':33 'trum':37 'truong':8,10,16 'tu':5C 'tung':11 'va':36 'van':12
25	The Last of Us: Những Người Còn Sót Lại	The Last of Us	the-last-of-us	20 năm sau khi đại dịch nấm Cordyceps tàn phá văn minh, người đàn ông chai sạn Joel được thuê đưa cô bé 14 tuổi Ellie vượt qua nước Mỹ hoang tàn.	/images/movies/movie-25-poster.webp	/images/movies/movie-25-backdrop.webp	https://www.youtube.com/watch?v=uLtkt8BonwM	8.9	320000	2023	60	18+	4K HDR	2	Âu Mỹ	5400000	t	f	Craig Mazin, Neil Druckmann	the last of us: nhung nguoi con sot lai the last of us craig mazin, neil druckmann pedro pascal bella ramsey gabriel luna anna torv 20 nam sau khi dai dich nam cordyceps tan pha van minh, nguoi dan ong chai san joel duoc thue dua co be 14 tuoi ellie vuot qua nuoc my hoang tan.	9	9	'14':49 '20':26 'anna':24 'be':48 'bella':20 'chai':41 'co':47 'con':7A 'cordyceps':33 'craig':14C 'dai':30 'dan':39 'dich':31 'druckmann':17C 'dua':46 'duoc':44 'ellie':51 'gabriel':22 'hoang':56 'joel':43 'khi':29 'lai':9A 'last':2A,11B 'luna':23 'mazin':15C 'minh':37 'my':55 'nam':27,32 'neil':16C 'nguoi':6A,38 'nhung':5A 'nuoc':54 'of':3A,12B 'ong':40 'pascal':19 'pedro':18 'pha':35 'qua':53 'ramsey':21 'san':42 'sau':28 'sot':8A 'tan':34,57 'the':1A,10B 'thue':45 'torv':25 'tuoi':50 'us':4A,13B 'van':36 'vuot':52
5	Solo Leveling: Tôi Thăng Cấp Một Mình (Mùa 2)	Solo Leveling: Arise from the Shadow	solo-leveling-season-2	Thợ săn yếu nhất thế giới Sung Jinwoo tiếp tục đánh thức sức mạnh Chúa Tể Bóng Tối, dẫn dắt đội quân bóng đêm càn quét các Hầm Ngục cấp S.	/images/movies/movie-5-poster.webp	/images/movies/movie-5-backdrop.webp	https://www.youtube.com/watch?v=byJ7pxxhaDY	8.8	195000	2025	24	16+	Full HD 1080p	3	Nhật Bản	4120000	t	f	Shunsuke Nakashige	solo leveling: toi thang cap mot minh (mua 2) solo leveling: arise from the shadow shunsuke nakashige taito ban genta nakamura reina ueda tho san yeu nhat the gioi sung jinwoo tiep tuc danh thuc suc manh chua te bong toi, dan dat doi quan bong dem can quet cac ham nguc cap s.	12	10	'2':9A 'arise':12B 'ban':19 'bong':40,46 'cac':50 'can':48 'cap':5A,53 'chua':38 'dan':42 'danh':34 'dat':43 'dem':47 'doi':44 'from':13B 'genta':20 'gioi':29 'ham':51 'jinwoo':31 'leveling':2A,11B 'manh':37 'minh':7A 'mot':6A 'mua':8A 'nakamura':21 'nakashige':17C 'nguc':52 'nhat':27 'quan':45 'quet':49 'reina':22 's':54 'san':25 'shadow':15B 'shunsuke':16C 'solo':1A,10B 'suc':36 'sung':30 'taito':18 'te':39 'thang':4A 'the':14B,28 'tho':24 'thuc':35 'tiep':32 'toi':3A,41 'tuc':33 'ueda':23 'yeu':26
11	Ký Sinh Trùng	Parasite	parasite	Một gia đình nghèo khó lập kế hoạch tinh vi để từng người một thâm nhập và làm việc cho một gia đình thượng lưu giàu có tại Seoul.	/images/movies/movie-11-poster.webp	/images/movies/movie-11-backdrop.webp	https://www.youtube.com/watch?v=isOGD_7hNIY	8.5	280000	2019	132	18+	Full HD 1080p	1	Hàn Quốc	3100000	f	f	Bong Joon-ho	ky sinh trung parasite bong joon-ho song kang-ho lee sun-kyun cho yeo-jeong choi woo-shik mot gia dinh ngheo kho lap ke hoach tinh vi de tung nguoi mot tham nhap va lam viec cho mot gia dinh thuong luu giau co tai seoul.	1	1	'bong':5C 'cho':17,44 'choi':21 'co':51 'de':35 'dinh':27,47 'gia':26,46 'giau':50 'ho':8C,12 'hoach':32 'jeong':20 'joon':7C 'joon-ho':6C 'kang':11 'kang-ho':10 'ke':31 'kho':29 'ky':1A 'kyun':16 'lam':42 'lap':30 'lee':13 'luu':49 'mot':25,38,45 'ngheo':28 'nguoi':37 'nhap':40 'parasite':4B 'seoul':53 'shik':24 'sinh':2A 'song':9 'sun':15 'sun-kyun':14 'tai':52 'tham':39 'thuong':48 'tinh':33 'trung':3A 'tung':36 'va':41 'vi':34 'viec':43 'woo':23 'woo-shik':22 'yeo':19 'yeo-jeong':18
34	Thợ Săn Quỷ Chainsaw Man	Chainsaw Man	chainsaw-man	Denji là một thanh niên nghèo khó hồi sinh dưới dạng Người Cưa sau khi hợp nhất với quỷ Pochita, gia nhập tổ chức thợ săn quỷ an toàn công cộng của Makima.	/images/movies/movie-34-poster.webp	/images/movies/movie-34-backdrop.webp	https://www.youtube.com/watch?v=q15CRdE5Bv0	8.6	230000	2022	24	18+	Full HD 1080p	3	Nhật Bản	4200000	f	f	Ryu Nakayama	tho san quy chainsaw man chainsaw man ryu nakayama kikunosuke toya tomori kusunoki shogo sakata fairouz ai denji la mot thanh nien ngheo kho hoi sinh duoi dang nguoi cua sau khi hop nhat voi quy pochita, gia nhap to chuc tho san quy an toan cong cong cua makima.	12	12	'ai':17 'an':45 'chainsaw':4A,6B 'chuc':41 'cong':47,48 'cua':30,49 'dang':28 'denji':18 'duoi':27 'fairouz':16 'gia':38 'hoi':25 'hop':33 'khi':32 'kho':24 'kikunosuke':10 'kusunoki':13 'la':19 'makima':50 'man':5A,7B 'mot':20 'nakayama':9C 'ngheo':23 'nguoi':29 'nhap':39 'nhat':34 'nien':22 'pochita':37 'quy':3A,36,44 'ryu':8C 'sakata':15 'san':2A,43 'sau':31 'shogo':14 'sinh':26 'thanh':21 'tho':1A,42 'to':40 'toan':46 'tomori':12 'toya':11 'voi':35
1	Xứ Cát: Phần Ba - Đấng Tiên Tri	Dune: Part Three - The Prophet	dune-part-three	Paul Atreides đối mặt với cuộc thánh chiến vũ trụ tàn khốc, gánh vác sứ mệnh tiên tri và số phận của toàn bộ vũ trụ khi các gia tộc hoàng gia trỗi dậy phản kháng.	/images/movies/movie-1-poster.webp	/images/movies/movie-1-backdrop.webp	https://www.youtube.com/watch?v=Way9Dexny3w	8.9	184500	2026	168	16+	4K UHD HDR	1	Âu Mỹ	1250004	t	t	Denis Villeneuve	xu cat: phan ba - dang tien tri dune: part three - the prophet denis villeneuve timothee chalamet zendaya florence pugh javier bardem anya taylor-joy paul atreides doi mat voi cuoc thanh chien vu tru tan khoc, ganh vac su menh tien tri va so phan cua toan bo vu tru khi cac gia toc hoang gia troi day phan khang.	1	1	'anya':22 'atreides':27 'ba':4A 'bardem':21 'bo':49 'cac':53 'cat':2A 'chalamet':16 'chien':33 'cua':47 'cuoc':31 'dang':5A 'day':59 'denis':13C 'doi':28 'dune':8B 'florence':18 'ganh':38 'gia':54,57 'hoang':56 'javier':20 'joy':25 'khang':61 'khi':52 'khoc':37 'mat':29 'menh':41 'part':9B 'paul':26 'phan':3A,46,60 'prophet':12B 'pugh':19 'so':45 'su':40 'tan':36 'taylor':24 'taylor-joy':23 'thanh':32 'the':11B 'three':10B 'tien':6A,42 'timothee':15 'toan':48 'toc':55 'tri':7A,43 'troi':58 'tru':35,51 'va':44 'vac':39 'villeneuve':14C 'voi':30 'vu':34,50 'xu':1A 'zendaya':17
16	Đất Rừng Phương Nam	Song of the South	dat-rung-phuong-nam	Hành trình phiêu lưu tìm cha đầy gian nan nhưng thấm đượm tình người của cậu bé An giữa miền Tây Nam Bộ hào hùng thời kỳ kháng chiến.	/images/movies/movie-16-poster.webp	/images/movies/movie-16-backdrop.webp	https://www.youtube.com/watch?v=hktzirCnJmQ	7.8	98000	2023	134	13+	4K UHD	1	Việt Nam	3200000	f	f	Nguyễn Quang Dũng	dat rung phuong nam song of the south nguyen quang dung hao khang tuan tran tran thanh tien luat hanh trinh phieu luu tim cha day gian nan nhung tham duom tinh nguoi cua cau be an giua mien tay nam bo hao hung thoi ky khang chien.	1	1	'an':37 'be':36 'bo':42 'cau':35 'cha':25 'chien':48 'cua':34 'dat':1A 'day':26 'dung':11C 'duom':31 'gian':27 'giua':38 'hanh':20 'hao':12,43 'hung':44 'khang':13,47 'ky':46 'luat':19 'luu':23 'mien':39 'nam':4A,41 'nan':28 'nguoi':33 'nguyen':9C 'nhung':29 'of':6B 'phieu':22 'phuong':3A 'quang':10C 'rung':2A 'song':5B 'south':8B 'tay':40 'tham':30 'thanh':17 'the':7B 'thoi':45 'tien':18 'tim':24 'tinh':32 'tran':15,16 'trinh':21 'tuan':14
9	Kẻ Trộm Giấc Mơ	Inception	inception	Dom Cobb là một đạo chích bậc thầy chuyên xâm nhập vào tiềm thức của mục tiêu trong khi họ đang mơ để đánh cắp những bí mật giá trị nhất.	/images/movies/movie-9-poster.webp	/images/movies/movie-9-backdrop.webp	https://www.youtube.com/watch?v=YoHD9XEInc0	8.8	390000	2010	148	13+	4K HDR	1	Âu Mỹ	4900000	f	f	Christopher Nolan	ke trom giac mo inception christopher nolan leonardo dicaprio joseph gordon-levitt elliot page tom hardy dom cobb la mot dao chich bac thay chuyen xam nhap vao tiem thuc cua muc tieu trong khi ho dang mo de danh cap nhung bi mat gia tri nhat.	1	1	'bac':24 'bi':44 'cap':42 'chich':23 'christopher':6C 'chuyen':26 'cobb':19 'cua':32 'dang':38 'danh':41 'dao':22 'de':40 'dicaprio':9 'dom':18 'elliot':14 'gia':46 'giac':3A 'gordon':12 'gordon-levitt':11 'hardy':17 'ho':37 'inception':5B 'joseph':10 'ke':1A 'khi':36 'la':20 'leonardo':8 'levitt':13 'mat':45 'mo':4A,39 'mot':21 'muc':33 'nhap':28 'nhat':48 'nhung':43 'nolan':7C 'page':15 'thay':25 'thuc':31 'tiem':30 'tieu':34 'tom':16 'tri':47 'trom':2A 'trong':35 'vao':29 'xam':27
21	Sát Thủ John Wick: Phần 4	John Wick: Chapter 4	john-wick-chapter-4	John Wick tìm ra con đường đánh bại Hội Đồng Tối Cao nhưng phải đối đầu với một kẻ thù mới có liên minh hùng mạnh trên toàn cầu.	/images/movies/movie-21-poster.webp	/images/movies/movie-21-backdrop.webp	https://www.youtube.com/watch?v=qEVUtrk8_B4	8.6	370000	2023	169	18+	4K UHD	1	Âu Mỹ	5600000	t	f	Chad Stahelski	sat thu john wick: phan 4 john wick: chapter 4 chad stahelski keanu reeves donnie yen bill skarsgard hiroyuki sanada john wick tim ra con duong danh bai hoi dong toi cao nhung phai doi dau voi mot ke thu moi co lien minh hung manh tren toan cau.	1	1	'4':6A,10B 'bai':28 'bill':17 'cao':32 'cau':49 'chad':11C 'chapter':9B 'co':42 'con':25 'danh':27 'dau':36 'doi':35 'dong':30 'donnie':15 'duong':26 'hiroyuki':19 'hoi':29 'hung':45 'john':3A,7B,21 'ke':39 'keanu':13 'lien':43 'manh':46 'minh':44 'moi':41 'mot':38 'nhung':33 'phai':34 'phan':5A 'ra':24 'reeves':14 'sanada':20 'sat':1A 'skarsgard':18 'stahelski':12C 'thu':2A,40 'tim':23 'toan':48 'toi':31 'tren':47 'voi':37 'wick':4A,8B,22 'yen':16
23	Phi Công Siêu Đẳng Maverick	Top Gun: Maverick	top-gun-maverick	Sau hơn 30 năm phục vụ, Pete Mitchell dẫn dắt một nhóm phi công trẻ tinh nhuệ thực hiện một phi vụ không kích nguy hiểm tột độ.	/images/movies/movie-23-poster.webp	/images/movies/movie-23-backdrop.webp	https://www.youtube.com/watch?v=giXco2jaZ_4	8.7	350000	2022	130	13+	4K IMAX	1	Âu Mỹ	4700000	f	f	Joseph Kosinski	phi cong sieu dang maverick top gun: maverick joseph kosinski tom cruise miles teller jennifer connelly jon hamm sau hon 30 nam phuc vu, pete mitchell dan dat mot nhom phi cong tre tinh nhue thuc hien mot phi vu khong kich nguy hiem tot do.	1	1	'30':21 'cong':2A,32 'connelly':16 'cruise':12 'dan':27 'dang':4A 'dat':28 'do':46 'gun':7B 'hamm':18 'hiem':44 'hien':37 'hon':20 'jennifer':15 'jon':17 'joseph':9C 'khong':41 'kich':42 'kosinski':10C 'maverick':5A,8B 'miles':13 'mitchell':26 'mot':29,38 'nam':22 'nguy':43 'nhom':30 'nhue':35 'pete':25 'phi':1A,31,39 'phuc':23 'sau':19 'sieu':3A 'teller':14 'thuc':36 'tinh':34 'tom':11 'top':6B 'tot':45 'tre':33 'vu':24,40
3	Arcane: Liên Minh Huyền Thoại (Mùa 2)	Arcane: League of Legends Season 2	arcane-season-2	Căng thẳng leo thang đỉnh điểm giữa thành phố tiến bộ Piltover và khu ổ chuột Zaun, đẩy hai chị em Vi và Jinx vào cuộc chiến sinh tử không lối thoát.	/images/movies/movie-3-poster.webp	/images/movies/movie-3-backdrop.webp	https://www.youtube.com/watch?v=ysqiEC6bLUI	9	289000	2024	45	16+	4K Dolby Vision	3	Âu Mỹ	3980000	t	f	Christian Linke, Alex Yee	arcane: lien minh huyen thoai (mua 2) arcane: league of legends season 2 christian linke, alex yee hailee steinfeld ella purnell katie leung cang thang leo thang dinh diem giua thanh pho tien bo piltover va khu o chuot zaun, day hai chi em vi va jinx vao cuoc chien sinh tu khong loi thoat.	9	9	'2':7A,13B 'alex':16C 'arcane':1A,8B 'bo':34 'cang':24 'chi':43 'chien':50 'christian':14C 'chuot':39 'cuoc':49 'day':41 'diem':29 'dinh':28 'ella':20 'em':44 'giua':30 'hai':42 'hailee':18 'huyen':4A 'jinx':47 'katie':22 'khong':53 'khu':37 'league':9B 'legends':11B 'leo':26 'leung':23 'lien':2A 'linke':15C 'loi':54 'minh':3A 'mua':6A 'o':38 'of':10B 'pho':32 'piltover':35 'purnell':21 'season':12B 'sinh':51 'steinfeld':19 'thang':25,27 'thanh':31 'thoai':5A 'thoat':55 'tien':33 'tu':52 'va':36,46 'vao':48 'vi':45 'yee':17C 'zaun':40
28	Đi Đến Nơi Có Gió	Meet Yourself	di-den-noi-co-gio	Hứa Hồng Đậu sau cú sốc mất bạn thân đã đến thôn Vân Miêu chữa lành tâm hồn, tìm lại ý nghĩa cuộc sống và tình yêu bên Tạ Chi Dao.	/images/movies/movie-28-poster.webp	/images/movies/movie-28-backdrop.webp	https://www.youtube.com/watch?v=9vHlPCF1idk	8.6	195000	2023	45	13+	Full HD 1080p	2	Trung Quốc	3800000	f	f	Đinh Tử Quang	di den noi co gio meet yourself dinh tu quang luu diec phi ly hien ho bang khanh nguu tuan phong hua hong dau sau cu soc mat ban than da den thon van mieu chua lanh tam hon, tim lai y nghia cuoc song va tinh yeu ben ta chi dao.	40	40	'ban':29 'bang':17 'ben':49 'chi':51 'chua':36 'co':4A 'cu':26 'cuoc':44 'da':31 'dao':52 'dau':24 'den':2A,32 'di':1A 'diec':12 'dinh':8C 'gio':5A 'hien':15 'ho':16 'hon':39 'hong':23 'hua':22 'khanh':18 'lai':41 'lanh':37 'luu':11 'ly':14 'mat':28 'meet':6B 'mieu':35 'nghia':43 'nguu':19 'noi':3A 'phi':13 'phong':21 'quang':10C 'sau':25 'soc':27 'song':45 'ta':50 'tam':38 'than':30 'thon':33 'tim':40 'tinh':47 'tu':9C 'tuan':20 'va':46 'van':34 'y':42 'yeu':48 'yourself':7B
29	Người Dơi (The Batman)	The Batman	the-batman	Trong năm thứ hai bảo vệ Gotham, Batman lần theo những manh mối bí ẩn của tên sát nhân Riddler nhằm vạch trần tệ nạn tham nhũng ăn sâu vào gia tộc Wayne.	/images/movies/movie-29-poster.webp	/images/movies/movie-29-backdrop.webp	https://www.youtube.com/watch?v=mqqft2x_Aa4	8.5	340000	2022	176	16+	4K HDR	1	Âu Mỹ	4600000	f	f	Matt Reeves	nguoi doi (the batman) the batman matt reeves robert pattinson zoe kravitz paul dano jeffrey wright trong nam thu hai bao ve gotham, batman lan theo nhung manh moi bi an cua ten sat nhan riddler nham vach tran te nan tham nhung an sau vao gia toc wayne.	1	1	'an':31,44 'bao':21 'batman':4A,6B,24 'bi':30 'cua':32 'dano':14 'doi':2A 'gia':47 'gotham':23 'hai':20 'jeffrey':15 'kravitz':12 'lan':25 'manh':28 'matt':7C 'moi':29 'nam':18 'nan':41 'nguoi':1A 'nham':37 'nhan':35 'nhung':27,43 'pattinson':10 'paul':13 'reeves':8C 'riddler':36 'robert':9 'sat':34 'sau':45 'te':40 'ten':33 'tham':42 'the':3A,5B 'theo':26 'thu':19 'toc':48 'tran':39 'trong':17 'vach':38 'vao':46 've':22 'wayne':49 'wright':16 'zoe':11
14	Lật Mặt 7: Một Điều Ước	Face Off 7: One Wish	lat-mat-7-mot-dieu-uoc	Câu chuyện xúc động về bà Hai 73 tuổi cùng năm người con đã trưởng thành, khi mẹ già gặp nạn ai sẽ là người phụng dưỡng chăm sóc.	/images/movies/movie-14-poster.webp	/images/movies/movie-14-backdrop.webp	https://www.youtube.com/watch?v=QdtPQ0wV53M	8.4	162000	2024	138	13+	4K UHD	1	Việt Nam	4850000	t	f	Lý Hải	lat mat 7: mot dieu uoc face off 7: one wish ly hai thanh hien truong minh cuong dinh y nhung quach ngoc tuyen cau chuyen xuc dong ve ba hai 73 tuoi cung nam nguoi con da truong thanh, khi me gia gap nan ai se la nguoi phung duong cham soc.	1	1	'7':3A,9B '73':32 'ai':46 'ba':30 'cau':25 'cham':52 'chuyen':26 'con':37 'cung':34 'cuong':18 'da':38 'dieu':5A 'dinh':19 'dong':28 'duong':51 'face':7B 'gap':44 'gia':43 'hai':13C,31 'hien':15 'khi':41 'la':48 'lat':1A 'ly':12C 'mat':2A 'me':42 'minh':17 'mot':4A 'nam':35 'nan':45 'ngoc':23 'nguoi':36,49 'nhung':21 'off':8B 'one':10B 'phung':50 'quach':22 'se':47 'soc':53 'thanh':14,40 'truong':16,39 'tuoi':33 'tuyen':24 'uoc':6A 've':29 'wish':11B 'xuc':27 'y':20
15	Quỷ Cẩu	Demon Dog	quy-cau	Lấy cảm hứng từ truyền thuyết linh dị dân gian Chó đội nón mê, kể về những tai ương rùng rợn giáng xuống một gia đình làm nghề mổ chó.	/images/movies/movie-15-poster.webp	/images/movies/movie-15-backdrop.webp	https://www.youtube.com/watch?v=Cswx649vXVw	7.5	82000	2023	105	18+	1080p FHD	1	Việt Nam	2150000	f	f	Lưu Thành Luân	quy cau demon dog luu thanh luan quang tuan nam thu nsnd kim xuan van dung lay cam hung tu truyen thuyet linh di dan gian cho doi non me, ke ve nhung tai uong rung ron giang xuong mot gia dinh lam nghe mo cho.	1	1	'cam':18 'cau':2A 'cho':27,46 'dan':25 'demon':3B 'di':24 'dinh':42 'dog':4B 'doi':28 'dung':16 'gia':41 'gian':26 'giang':38 'hung':19 'ke':31 'kim':13 'lam':43 'lay':17 'linh':23 'luan':7C 'luu':5C 'me':30 'mo':45 'mot':40 'nam':10 'nghe':44 'nhung':33 'non':29 'nsnd':12 'quang':8 'quy':1A 'ron':37 'rung':36 'tai':34 'thanh':6C 'thu':11 'thuyet':22 'truyen':21 'tu':20 'tuan':9 'uong':35 'van':15 've':32 'xuan':14 'xuong':39
17	Exhuma: Quật Mộ Trùng Ma	Exhuma	exhuma-quat-mo-trung-ma	Hai pháp sư trừ tà trẻ tuổi cùng thầy phong thủy và chuyên gia mai táng nhận lời khai quật ngôi mộ tổ tiên của một gia tộc giàu có, vô tình giải phóng ác quỷ cổ xưa.	/images/movies/movie-17-poster.webp	/images/movies/movie-17-backdrop.webp	https://www.youtube.com/watch?v=66K9-l0EkE0	8.4	189000	2024	134	16+	4K HDR	1	Hàn Quốc	3920000	t	f	Jang Jae-hyun	exhuma: quat mo trung ma exhuma jang jae-hyun choi min-sik kim go-eun yoo hae-jin lee do-hyun hai phap su tru ta tre tuoi cung thay phong thuy va chuyen gia mai tang nhan loi khai quat ngoi mo to tien cua mot gia toc giau co, vo tinh giai phong ac quy co xua.	1	1	'ac':61 'choi':11 'chuyen':39 'co':56,63 'cua':51 'cung':34 'do':25 'do-hyun':24 'eun':18 'exhuma':1A,6B 'gia':40,53 'giai':59 'giau':55 'go':17 'go-eun':16 'hae':21 'hae-jin':20 'hai':27 'hyun':10C,26 'jae':9C 'jae-hyun':8C 'jang':7C 'jin':22 'khai':45 'kim':15 'lee':23 'loi':44 'ma':5A 'mai':41 'min':13 'min-sik':12 'mo':3A,48 'mot':52 'ngoi':47 'nhan':43 'phap':28 'phong':36,60 'quat':2A,46 'quy':62 'sik':14 'su':29 'ta':31 'tang':42 'thay':35 'thuy':37 'tien':50 'tinh':58 'to':49 'toc':54 'tre':32 'tru':30 'trung':4A 'tuoi':33 'va':38 'vo':57 'xua':64 'yoo':19
24	Avatar: Dòng Chảy Của Nước	Avatar: The Way of Water	avatar-the-way-of-water	Jake Sully và Neytiri cùng các con phải rời bỏ rừng già, tìm nơi ẩn náu tại các bộ tộc sống ven biển Pandora khi Người Trời quay lại xâm chiếm.	/images/movies/movie-24-poster.webp	/images/movies/movie-24-backdrop.webp	https://www.youtube.com/watch?v=d9MyW72ELq0	8.5	410000	2022	192	13+	4K 3D HFR	1	Âu Mỹ	6800000	f	f	James Cameron	avatar: dong chay cua nuoc avatar: the way of water james cameron sam worthington zoe saldana sigourney weaver stephen lang jake sully va neytiri cung cac con phai roi bo rung gia, tim noi an nau tai cac bo toc song ven bien pandora khi nguoi troi quay lai xam chiem.	1	1	'an':35 'avatar':1A,6B 'bien':43 'bo':30,39 'cac':26,38 'cameron':12C 'chay':3A 'chiem':51 'con':27 'cua':4A 'cung':25 'dong':2A 'gia':32 'jake':21 'james':11C 'khi':45 'lai':49 'lang':20 'nau':36 'neytiri':24 'nguoi':46 'noi':34 'nuoc':5A 'of':9B 'pandora':44 'phai':28 'quay':48 'roi':29 'rung':31 'saldana':16 'sam':13 'sigourney':17 'song':41 'stephen':19 'sully':22 'tai':37 'the':7B 'tim':33 'toc':40 'troi':47 'va':23 'ven':42 'water':10B 'way':8B 'weaver':18 'worthington':14 'xam':50 'zoe':15
33	Frieren: Pháp Sư Tiễn Táng	Frieren: Beyond Journey's End	frieren-beyond-journeys-end	Sau khi đánh bại Quỷ Vương, pháp sư tộc elf Frieren bất tử bắt đầu hành trình mới để thấu hiểu con người và những ký ức vô giá về các chiến hữu đã khuất.	/images/movies/movie-33-poster.webp	/images/movies/movie-33-backdrop.webp	https://www.youtube.com/watch?v=Iwr1aLEDpe4	9.3	295000	2024	24	13+	Full HD 1080p	3	Nhật Bản	4600001	t	f	Keiichiro Saito	frieren: phap su tien tang frieren: beyond journey's end keiichiro saito atsumi tanezaki nobuhiko okamoto hiroki touchi kana ichinose sau khi danh bai quy vuong, phap su toc elf frieren bat tu bat dau hanh trinh moi de thau hieu con nguoi va nhung ky uc vo gia ve cac chien huu da khuat.	28	28	'atsumi':13 'bai':24 'bat':32,34 'beyond':7B 'cac':51 'chien':52 'con':42 'da':54 'danh':23 'dau':35 'de':39 'elf':30 'end':10B 'frieren':1A,6B,31 'gia':49 'hanh':36 'hieu':41 'hiroki':17 'huu':53 'ichinose':20 'journey':8B 'kana':19 'keiichiro':11C 'khi':22 'khuat':55 'ky':46 'moi':38 'nguoi':43 'nhung':45 'nobuhiko':15 'okamoto':16 'phap':2A,27 'quy':25 's':9B 'saito':12C 'sau':21 'su':3A,28 'tanezaki':14 'tang':5A 'thau':40 'tien':4A 'toc':29 'touchi':18 'trinh':37 'tu':33 'uc':47 'va':44 've':50 'vo':48 'vuong':26
37	Vành Đai Đỏ	The Red Orbit	vanh-dai-do	Một phi hành đoàn cứu hộ nhận được tín hiệu bí ẩn từ trạm nghiên cứu bỏ hoang ngoài quỹ đạo Sao Hỏa. Khi tiếp cận, họ phát hiện ký ức của chính mình đang bị tái tạo bên trong trạm.	/images/movies/movie-37-poster.webp	/images/movies/movie-37-backdrop.webp	https://www.youtube.com/watch?v=Yf3VreXwVpI	8.8	86420	2026	48	16+	4K Dolby Vision	2	Âu Mỹ	1684000	t	t	Lena Hartmann	vanh dai do the red orbit lena hartmann mara ellis jonas vale kei tanaka amara cole mot phi hanh doan cuu ho nhan duoc tin hieu bi an tu tram nghien cuu bo hoang ngoai quy dao sao hoa. khi tiep can, ho phat hien ky uc cua chinh minh dang bi tai tao ben trong tram.	8	8	'amara':15 'an':28 'ben':55 'bi':27,52 'bo':33 'can':42 'chinh':49 'cole':16 'cua':48 'cuu':21,32 'dai':2A 'dang':51 'dao':37 'do':3A 'doan':20 'duoc':24 'ellis':10 'hanh':19 'hartmann':8C 'hien':45 'hieu':26 'ho':22,43 'hoa':39 'hoang':34 'jonas':11 'kei':13 'khi':40 'ky':46 'lena':7C 'mara':9 'minh':50 'mot':17 'nghien':31 'ngoai':35 'nhan':23 'orbit':6B 'phat':44 'phi':18 'quy':36 'red':5B 'sao':38 'tai':53 'tanaka':14 'tao':54 'the':4B 'tiep':41 'tin':25 'tram':30,57 'trong':56 'tu':29 'uc':47 'vale':12 'vanh':1A
30	Phong Thần Tam Bộ Khúc: Yêu Loạn Thương Chu	Creation of the Gods I: Kingdom of Storms	phong-than-1	Sử thi kỳ ảo hoành tráng tái hiện cuộc đối đầu huyền thoại giữa Trụ Vương, Đát Kỷ và các dũng sĩ Cơ Phát, Khương Tử Nha thời cổ đại.	/images/movies/movie-30-poster.webp	/images/movies/movie-30-backdrop.webp	https://www.youtube.com/watch?v=_a8j3NUJiQ0	8.3	185000	2023	148	16+	4K IMAX	1	Trung Quốc	3500000	f	f	Ô Nhĩ Thiện	phong than tam bo khuc: yeu loan thuong chu creation of the gods i: kingdom of storms o nhi thien phi tuong ly tuyet kien hoang bot naran su thi ky ao hoanh trang tai hien cuoc doi dau huyen thoai giua tru vuong, dat ky va cac dung si co phat, khuong tu nha thoi co dai.	1	1	'ao':32 'bo':4A 'bot':27 'cac':48 'chu':9A 'co':51,57 'creation':10B 'cuoc':37 'dai':58 'dat':45 'dau':39 'doi':38 'dung':49 'giua':42 'gods':13B 'hien':36 'hoang':26 'hoanh':33 'huyen':40 'i':14B 'khuc':5A 'khuong':53 'kien':25 'kingdom':15B 'ky':31,46 'loan':7A 'ly':23 'naran':28 'nha':55 'nhi':19C 'o':18C 'of':11B,16B 'phat':52 'phi':21 'phong':1A 'si':50 'storms':17B 'su':29 'tai':35 'tam':3A 'than':2A 'the':12B 'thi':30 'thien':20C 'thoai':41 'thoi':56 'thuong':8A 'trang':34 'tru':43 'tu':54 'tuong':22 'tuyet':24 'va':47 'vuong':44 'yeu':6A
39	Biên Niên Sử Aether	Aether Chronicle	bien-nien-su-aether	Trên những hòn đảo trôi giữa tầng mây, một học viên chế tạo trẻ tuổi vô tình đánh thức cổ vật có thể điều khiển bầu trời và trở thành mục tiêu của đế chế đang thống trị thế giới.	/images/movies/movie-39-poster.webp	/images/movies/movie-39-backdrop.webp	https://www.youtube.com/watch?v=iaJ4VVFGIa8	8.7	121300	2026	25	13+	Full HD 1080p	3	Nhật Bản	2140000	t	f	Akira Senda	bien nien su aether aether chronicle akira senda riku aoyama mio kisaragi daichi mori hana ueda tren nhung hon dao troi giua tang may, mot hoc vien che tao tre tuoi vo tinh danh thuc co vat co the dieu khien bau troi va tro thanh muc tieu cua de che dang thong tri the gioi.	10	10	'aether':4A,5B 'akira':7C 'aoyama':10 'bau':42 'bien':1A 'che':28,51 'chronicle':6B 'co':36,38 'cua':49 'daichi':13 'dang':52 'danh':34 'dao':20 'de':50 'dieu':40 'gioi':56 'giua':22 'hana':15 'hoc':26 'hon':19 'khien':41 'kisaragi':12 'may':24 'mio':11 'mori':14 'mot':25 'muc':47 'nhung':18 'nien':2A 'riku':9 'senda':8C 'su':3A 'tang':23 'tao':29 'thanh':46 'the':39,55 'thong':53 'thuc':35 'tieu':48 'tinh':33 'tre':30 'tren':17 'tri':54 'tro':45 'troi':21,43 'tuoi':31 'ueda':16 'va':44 'vat':37 'vien':27 'vo':32
\.


--
-- Data for Name: notifications; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.notifications ("Id", "UserId", "Title", "Copy", "Icon", "ActionUrl", "Read", "CreatedAtUtc") FROM stdin;
011cea31-11f2-40d8-bb7d-32f098fa5d6f	11111111-1111-1111-1111-111111111111	CinemaFlow VIP	Máy chủ VIP 4K mới đã sẵn sàng cho tài khoản của bạn.	Crown	/vip	t	2026-11-02 13:52:07.597434+00
6fdaae18-a3fc-48ef-ad0d-93db37088bd2	11111111-1111-1111-1111-111111111111	Tập mới đã lên sóng	Xứ Cát: Phần Ba vừa cập nhật bản chiếu rạp đầy đủ 4K HDR.	Play	/watch/1	f	2026-11-03 13:40:07.597434+00
\.


--
-- Data for Name: password_reset_tokens; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.password_reset_tokens ("Token", "UserId", "ExpiresAtUtc", "UsedAtUtc") FROM stdin;
\.


--
-- Data for Name: payments; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.payments ("Id", "OrderCode", "UserId", "PlanId", "PlanName", "Amount", "Method", "Status", "CreatedAtUtc", "PaidAtUtc", "VipExpiresAtUtc", "TransactionReference", "Note") FROM stdin;
4e5fe842-ee79-4067-b9b5-a92a203be12c	CF20260318VIP30FAILED	33333333-3333-3333-3333-333333333333	vip-30	VIP 1 Tháng	79000.00	2	3	2026-11-02 13:52:07.597434+00	\N	\N	\N	Từ chối duyệt: Giao dịch MoMo bị hủy hoặc quá hạn thanh toán
98e7b130-3cbf-423a-9bb9-23c4cd3116d4	CF20260301VIP30MEMBER	33333333-3333-3333-3333-333333333333	vip-30	VIP 30 ngày	59000.00	3	2	2026-10-19 13:52:07.597434+00	2026-10-19 13:52:07.597434+00	2026-12-03 13:52:07.597434+00	\N	\N
ca914573-626d-45c9-a7c0-8bee58a12727	CF20260101VIP365ADMIN	11111111-1111-1111-1111-111111111111	vip-365	VIP 365 ngày	499000.00	1	2	2026-05-03 13:52:07.597434+00	2026-05-03 13:52:07.597434+00	2027-11-03 13:52:07.597434+00	\N	\N
ce446de1-c489-4ddf-8850-949dfb60c3c1	CF20260320VIP180PENDING	22222222-2222-2222-2222-222222222222	vip-180	VIP 6 Tháng	349000.00	1	1	2026-11-03 11:52:07.597434+00	\N	\N	FT2609081239MB	Chuyển khoản VietQR chờ đối soát sao kê ngân hàng
edcdfd4d-6bb1-4b8d-aad1-ae3b05209e65	CF20260215VIP90NAM	22222222-2222-2222-2222-222222222222	vip-90	VIP 90 ngày	149000.00	2	2	2026-10-31 13:52:07.597434+00	2026-10-31 13:52:07.597434+00	2027-02-01 13:52:07.597434+00	\N	\N
\.


--
-- Data for Name: playback_issues; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.playback_issues ("Id", "UserId", "MovieId", "EpisodeId", "Description", "CreatedAtUtc") FROM stdin;
\.


--
-- Data for Name: playback_telemetry; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.playback_telemetry ("Id", "UserId", "MovieId", "EpisodeId", "SessionId", "Position", "ServerName", "Quality", "EventType", "Value", "ErrorCode", "CreatedAtUtc") FROM stdin;
1	11111111-1111-1111-1111-111111111111	1	101	bea704a82b4e4e90af00ab78e76933cb	7	VIP 2 (Sài Gòn - Ổn Định)	720p HD	rebuffer	4799	\N	2026-11-03 19:10:22.250621+00
2	11111111-1111-1111-1111-111111111111	1	101	98775a071614404e92259a062b3505e3	2	VIP 1 (Hà Nội - Siêu Tốc)	1080p 60fps	rebuffer	2076	\N	2026-11-03 19:10:42.042393+00
3	11111111-1111-1111-1111-111111111111	1	101	98775a071614404e92259a062b3505e3	2	VIP 1 (Hà Nội - Siêu Tốc)	1080p 60fps	rebuffer	2797	\N	2026-11-03 19:10:46.838761+00
4	11111111-1111-1111-1111-111111111111	1	101	98775a071614404e92259a062b3505e3	2	VIP 1 (Hà Nội - Siêu Tốc)	1080p 60fps	rebuffer	1894	\N	2026-11-03 19:10:52.957086+00
5	11111111-1111-1111-1111-111111111111	1	101	0a29759d7e8b434d908384ebc3147294	4	VIP 1 (Hà Nội - Siêu Tốc)	720p HD	rebuffer	3219	\N	2026-11-03 19:11:57.75689+00
\.


--
-- Data for Name: reviews; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.reviews ("Id", "MovieId", "UserId", "Rating", "Comment", "AdminReply", "AdminRepliedAtUtc", "IsReported", "CreatedAtUtc", "UpdatedAtUtc") FROM stdin;
159331fb-30ef-4f78-be9b-927ea062fdb6	3	33333333-3333-3333-3333-333333333333	9	Mối quan hệ giữa Vi và Jinx đau lòng đến nghẹn thở. Phim hoạt hình xuất sắc nhất thập kỷ.	\N	\N	f	2026-11-01 13:52:07.597434+00	2026-11-01 13:52:07.597434+00
17fc8667-1a95-4ecc-a9df-95923f47451b	1	44444444-4444-4444-4444-444444444444	9	Diễn xuất của Timothée Chalamet và Zendaya ngày càng chín muồi. Một cái kết đầy ám ảnh.	\N	\N	f	2026-10-29 13:52:07.597434+00	2026-10-29 13:52:07.597434+00
2291a7f7-6a76-4d96-a924-f888170f42cf	1	22222222-2222-2222-2222-222222222222	9	Cảnh đại chiến cát sa mạc quá choáng ngợp. Âm nhạc của Hans Zimmer rung chuyển cả phòng chiếu, xứng đáng xem bản 4K HDR.	\N	\N	f	2026-10-26 13:52:07.597434+00	2026-10-26 13:52:07.597434+00
38d03c2f-092b-45f8-b93f-fe0e476d68c3	1	11111111-1111-1111-1111-111111111111	10	Đỉnh cao điện ảnh khoa học viễn tưởng! Denis Villeneuve đã khép lại thiên anh hùng ca Xứ Cát bằng một kiệt tác hoàn hảo từ âm thanh, kỹ xảo đến chiều sâu tư tưởng.	\N	\N	f	2026-10-24 13:52:07.597434+00	2026-10-24 13:52:07.597434+00
3fa84165-7354-40ba-b304-75058ebfce52	17	11111111-1111-1111-1111-111111111111	9	Không khí kinh dị tâm linh rùng rợn, lồng ghép lịch sử tinh tế. Choi Min-sik và Kim Go-eun phối hợp ăn ý.	\N	\N	f	2026-10-23 13:52:07.597434+00	2026-10-23 13:52:07.597434+00
5a689375-3108-4114-a646-8209d6286a83	5	55555555-5555-5555-5555-555555555555	9	Phần 2 hành động mãn nhãn, đội quân bóng đêm trỗi dậy ngầu đét! Đồ họa của A-1 Pictures rất mượt.	\N	\N	f	2026-10-31 13:52:07.597434+00	2026-10-31 13:52:07.597434+00
7c8b542e-c5ca-467e-a2e9-9e74e4287c12	2	55555555-5555-5555-5555-555555555555	9	Nolan tiếp tục chứng minh khả năng kể chuyện phi tuyến tính bậc thầy. Thoại và âm nhạc phối hợp không một vết gợn.	\N	\N	f	2026-10-25 13:52:07.597434+00	2026-10-25 13:52:07.597434+00
817c4eb5-f0d5-494b-b312-2db8528451ed	3	44444444-4444-4444-4444-444444444444	10	Mùa 2 vượt ngoài mọi kỳ vọng! Phần hoạt họa của Fortiche đẹp như tranh vẽ, âm nhạc quá cảm xúc.	\N	\N	f	2026-10-30 13:52:07.597434+00	2026-10-30 13:52:07.597434+00
b2e4e8b1-13bb-4f26-b321-075c95f7f6f5	13	33333333-3333-3333-3333-333333333333	8	Phương Anh Đào diễn xuất rất có hồn, ánh mắt chất chứa nỗi niềm. Góc quay và ánh sáng tiến bộ rõ rệt.	\N	\N	f	2026-10-28 13:52:07.597434+00	2026-10-28 13:52:07.597434+00
c077e82f-cb2a-4d65-9a43-85981a23efbb	2	22222222-2222-2222-2222-222222222222	10	Cillian Murphy xứng đáng 10/10 với ánh mắt đầy dằn vặt. Đoạn thử nghiệm Trinity khiến cả phòng nín thở.	\N	\N	f	2026-10-22 13:52:07.597434+00	2026-10-22 13:52:07.597434+00
d038061d-c6e7-45d6-9b24-a86b7ef13fc7	11	44444444-4444-4444-4444-444444444444	10	Kịch bản xuất sắc không một hạt sạn. Phản ánh chân thực sự phân hóa giàu nghèo sâu sắc.	\N	\N	f	2026-10-09 13:52:07.597434+00	2026-10-09 13:52:07.597434+00
d4f5a0ff-34b6-4e37-8e2e-d985efeb8cca	6	22222222-2222-2222-2222-222222222222	9	Tái hiện văn hóa võ sĩ đạo và mưu lược chính trị thời phong kiến Nhật Bản chuẩn chỉ đến từng chi tiết nhỏ.	\N	\N	f	2026-10-19 13:52:07.597434+00	2026-10-19 13:52:07.597434+00
df925306-a926-4ff6-a318-e081fb8d4111	14	22222222-2222-2222-2222-222222222222	9	Lấy đi rất nhiều nước mắt. Cảnh bà Hai ngồi chờ các con nơi quê nhà làm ai xa quê cũng phải quặn lòng.	\N	\N	f	2026-10-27 13:52:07.597434+00	2026-10-27 13:52:07.597434+00
f055839c-00ff-4b36-96a8-6d0861b0ecf7	13	44444444-4444-4444-4444-444444444444	9	Một bộ phim chạm đến cảm xúc của nhiều người phụ nữ hiện đại. Nhạc phim của Phan Mạnh Quỳnh rất hay.	\N	\N	f	2026-10-30 13:52:07.597434+00	2026-10-30 13:52:07.597434+00
f35b88a6-65bc-4651-ba73-55bad28a4147	4	11111111-1111-1111-1111-111111111111	10	Heath Ledger với vai Joker là huyền thoại sống mãi trong lịch sử điện ảnh thế giới.	\N	\N	f	2026-10-14 13:52:07.597434+00	2026-10-14 13:52:07.597434+00
\.


--
-- Data for Name: security_audit_events; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.security_audit_events ("Id", "UserId", "EventType", "Success", "Detail", "CreatedAtUtc") FROM stdin;
83f4dbcc-8327-4b4a-99b2-f9ba13c36ed4	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-03 13:54:36.290054+00
f7cc5083-e7a8-4775-a7bf-f53236302528	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-03 14:15:17.401339+00
7c4bbb50-adee-46e7-8e79-aa5afb5d0de7	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-03 18:57:28.596232+00
db490d33-a91d-4119-8ac1-91a6aaf42374	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-03 19:09:33.689932+00
e1ca3d7d-cb81-4028-856d-6b450e695b3d	\N	login.failed	f	Email: kynhungvanlaky@gmail.com	2026-11-03 19:11:04.280512+00
26076e89-fb45-4ecf-b2dd-d8f9bdeebdcd	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-03 19:11:05.488121+00
27fa86b2-ce99-45cf-97ed-7903427d7799	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-04 04:27:57.196897+00
7a5a8c96-e4e3-498e-92d1-9da8af216138	11111111-1111-1111-1111-111111111111	login.success	t	Email: admin@cinemaflow.vn	2026-11-04 04:28:36.869354+00
\.


--
-- Data for Name: stream_sources; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.stream_sources ("EpisodeId", "Position", "ServerName", "ServerType", "Quality", "StreamUrl") FROM stdin;
101	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
101	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
101	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
101	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
101	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
201	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
201	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
201	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
301	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
301	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
301	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
302	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
302	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
302	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
303	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
303	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
303	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
101	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
101	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
201	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
201	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
201	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
301	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
301	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
301	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
302	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
302	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
303	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
303	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
303	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
101	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
101	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
101	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
201	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
301	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
301	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
302	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
302	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
303	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
304	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
304	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
304	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
305	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
305	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
305	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
306	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
306	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
306	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
307	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
307	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
307	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
308	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
308	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
308	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
304	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
304	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
304	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
305	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
305	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
306	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
306	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
306	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
304	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
304	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
305	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
305	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
306	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
307	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
308	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
307	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
401	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
308	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4101	1	VIP 1 (R2 Tivi Android - 185MB)	1	1080p FHD	r2://webxemphim-movies/Hu%CC%9Bo%CC%81ng%20Da%CC%82%CC%83n%20Ca%CC%80i%20Giao%20Die%CC%A3%CC%82n%20Sa%CC%A3ch%2C%20Xoa%CC%81%20App%20Ra%CC%81c%20Cho%20Tivi%20Android%20Cu%CC%9B%CC%A3c%20De%CC%82%CC%83%20-%20Kho%CC%82ng%20Ca%CC%82%CC%80n%20Laptop%2C%20AI%21.mp4
4101	2	VIP 1 (R2 Tivi Android - 185MB)	1	720p HD	r2://webxemphim-movies/Hu%CC%9Bo%CC%81ng%20Da%CC%82%CC%83n%20Ca%CC%80i%20Giao%20Die%CC%A3%CC%82n%20Sa%CC%A3ch%2C%20Xoa%CC%81%20App%20Ra%CC%81c%20Cho%20Tivi%20Android%20Cu%CC%9B%CC%A3c%20De%CC%82%CC%83%20-%20Kho%CC%82ng%20Ca%CC%82%CC%80n%20Laptop%2C%20AI%21.mp4
4101	5	VIP 2 (R2 Backup - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4101	8	Hydra CDN (R2 Tivi Android)	2	1080p FHD	r2://webxemphim-movies/Hu%CC%9Bo%CC%81ng%20Da%CC%82%CC%83n%20Ca%CC%80i%20Giao%20Die%CC%A3%CC%82n%20Sa%CC%A3ch%2C%20Xoa%CC%81%20App%20Ra%CC%81c%20Cho%20Tivi%20Android%20Cu%CC%9B%CC%A3c%20De%CC%82%CC%83%20-%20Kho%CC%82ng%20Ca%CC%82%CC%80n%20Laptop%2C%20AI%21.mp4
4101	9	Hydra CDN (R2 Tivi Android)	2	720p HD	r2://webxemphim-movies/Hu%CC%9Bo%CC%81ng%20Da%CC%82%CC%83n%20Ca%CC%80i%20Giao%20Die%CC%A3%CC%82n%20Sa%CC%A3ch%2C%20Xoa%CC%81%20App%20Ra%CC%81c%20Cho%20Tivi%20Android%20Cu%CC%9B%CC%A3c%20De%CC%82%CC%83%20-%20Kho%CC%82ng%20Ca%CC%82%CC%80n%20Laptop%2C%20AI%21.mp4
4101	11	Direct Backup (R2 Tivi Android)	3	720p HD	r2://webxemphim-movies/Hu%CC%9Bo%CC%81ng%20Da%CC%82%CC%83n%20Ca%CC%80i%20Giao%20Die%CC%A3%CC%82n%20Sa%CC%A3ch%2C%20Xoa%CC%81%20App%20Ra%CC%81c%20Cho%20Tivi%20Android%20Cu%CC%9B%CC%A3c%20De%CC%82%CC%83%20-%20Kho%CC%82ng%20Ca%CC%82%CC%80n%20Laptop%2C%20AI%21.mp4
4201	1	VIP 1 (R2 TikTok - 12.6MB)	1	1080p FHD	r2://webxemphim-movies/%23xuhuong%20%23xuhuongtiktok%20%23xuhuongtiktok2026%20%23vieclam%20%23vieclamhanam%20.mp4
4201	5	VIP 2 (R2 Backup - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4201	8	Hydra CDN (R2 TikTok)	2	1080p FHD	r2://webxemphim-movies/%23xuhuong%20%23xuhuongtiktok%20%23xuhuongtiktok2026%20%23vieclam%20%23vieclamhanam%20.mp4
4201	11	Direct Backup (R2 TikTok)	3	720p HD	r2://webxemphim-movies/%23xuhuong%20%23xuhuongtiktok%20%23xuhuongtiktok2026%20%23vieclam%20%23vieclamhanam%20.mp4
4301	1	VIP 1 (R2 Video.mp4 - 3.9MB)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4301	5	VIP 2 (R2 Video.mp4 - 3.9MB)	1	720p HD	r2://webxemphim-movies/Video.mp4
4301	8	Hydra CDN (R2 Video.mp4)	2	1080p FHD	r2://webxemphim-movies/Video.mp4
4301	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4401	1	VIP 1 (R2 play.mp4 - 72.8MB)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4401	2	VIP 1 (R2 play.mp4 - 72.8MB)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4401	5	VIP 2 (R2 Backup - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4401	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4401	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4401	11	Direct Backup (R2 play.mp4)	3	720p HD	r2://webxemphim-movies/play.mp4
4501	1	VIP 1 (R2 Meyo MP3 - 4.2MB)	1	Lossless Audio	r2://webxemphim-movies/Meyo%20-%20Half%20Asleep%20%28Lyrics%29.mp3
4501	8	Hydra CDN (R2 Meyo MP3)	2	Lossless Audio	r2://webxemphim-movies/Meyo%20-%20Half%20Asleep%20%28Lyrics%29.mp3
4501	11	Direct Backup (R2 Meyo MP3)	3	320kbps Audio	r2://webxemphim-movies/Meyo%20-%20Half%20Asleep%20%28Lyrics%29.mp3
309	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
309	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
309	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
401	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
401	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
401	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
501	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
501	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
501	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
502	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
502	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
502	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
503	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
503	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
503	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
309	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
309	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
501	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
401	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
401	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
502	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
501	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
501	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
309	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
401	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
502	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
503	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
501	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
502	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
505	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
506	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
503	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
504	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
504	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
504	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
505	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
505	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
505	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
506	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
506	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
506	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
507	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
507	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
507	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
508	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
508	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
508	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
504	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
504	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
507	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
505	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
505	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
508	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
506	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
506	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
511	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
507	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
504	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
505	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
508	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
509	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
506	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
511	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
507	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
512	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
508	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
509	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
509	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
509	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
510	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
510	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
510	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
511	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
511	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
511	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
512	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
512	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
512	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
601	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
601	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
601	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
509	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
509	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
601	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
510	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
510	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
603	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
511	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
511	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
604	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
509	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
601	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
602	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
510	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
511	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
604	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
605	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
512	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
601	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
602	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
602	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
602	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
603	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
603	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
603	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
604	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
604	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
604	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
605	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
605	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
605	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
606	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
606	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
606	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
602	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
602	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
607	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
603	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
603	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
609	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
610	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
604	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
602	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
608	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
603	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
610	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
604	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
701	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
605	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
606	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
101	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
607	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
607	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
607	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
608	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
608	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
608	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
609	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
609	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
609	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
610	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
610	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
610	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
701	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
701	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
701	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
607	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
607	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
608	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
608	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
804	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
609	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
609	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
805	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
610	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
607	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
607	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
802	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
803	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
608	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
609	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
805	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
806	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
610	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
701	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
801	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
801	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
801	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
802	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
802	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
802	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
803	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
803	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
803	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
804	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
804	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
804	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
805	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
805	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
805	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
801	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
801	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
808	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
802	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
802	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
901	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
803	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
803	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1002	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
804	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
804	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1003	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
805	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
805	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
801	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
808	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
802	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
901	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1001	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
803	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
804	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1003	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1004	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
805	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
201	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
806	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
806	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
806	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
807	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
807	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
807	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
808	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
808	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
808	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
901	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
901	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
901	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1001	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1001	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1001	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
806	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
806	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1005	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
807	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
807	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1006	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
808	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
808	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1008	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1101	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
806	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
807	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1006	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
808	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
901	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1201	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1001	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1002	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1002	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1002	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1003	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1003	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1003	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1004	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1004	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1004	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1005	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1005	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1005	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1006	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1006	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1006	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1002	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1002	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1203	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1003	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1003	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1204	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1004	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1004	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1202	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1002	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1003	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1204	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1205	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1004	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1005	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1207	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1006	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1007	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1007	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1007	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1007	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1008	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1008	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1008	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1101	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1101	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1101	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1201	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1201	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1201	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1202	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1202	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1202	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1007	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1007	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1008	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1008	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1210	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1101	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1101	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1201	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1007	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1007	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1208	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1008	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1210	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1101	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1211	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1201	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1213	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1202	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1203	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1203	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1203	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1204	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1204	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1204	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1205	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1205	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1205	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1206	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1206	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1206	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1207	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1207	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1207	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1203	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1203	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1215	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1204	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1204	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1216	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1205	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1205	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1214	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1203	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1204	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1216	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1217	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1205	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1206	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1219	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1207	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1208	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1208	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1208	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1209	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1209	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1209	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1210	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1210	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1210	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1211	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1211	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1211	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1212	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1212	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1212	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1208	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1208	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1221	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1209	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1209	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1222	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1210	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1210	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1211	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1220	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1208	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1209	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1222	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1223	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1210	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1301	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1211	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1212	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1213	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1213	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1213	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1214	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1214	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1214	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1215	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1215	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1215	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1216	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1216	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1216	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1217	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1217	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1217	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1218	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1213	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1213	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1214	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1214	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1701	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1801	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1215	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1215	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1216	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1216	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1803	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1804	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1217	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1217	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1501	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1213	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1601	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1214	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1801	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1215	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1802	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1216	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1804	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1217	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1218	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1218	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1219	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1219	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1219	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1220	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1220	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1220	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1221	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1221	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1221	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1222	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1222	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1222	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1218	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1218	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1806	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1219	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1219	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1902	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1220	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1220	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1903	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1221	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1805	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1218	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1901	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1219	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1220	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1903	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1904	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1221	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1222	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1223	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1223	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1223	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1301	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1301	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1301	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1401	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1401	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1401	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1501	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1501	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1501	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1601	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1601	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1601	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1223	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1223	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1906	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1301	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1301	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1908	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1401	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1401	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1909	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1223	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1906	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1907	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1301	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1401	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1909	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1910	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1501	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1601	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1701	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1701	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1701	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1801	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1801	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1801	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1802	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1802	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1802	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1803	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1803	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1803	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1804	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1804	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1804	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1701	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1701	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1912	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1801	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1801	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1914	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1802	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1802	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1701	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1912	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1913	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1801	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1802	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1915	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1916	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1803	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1804	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1805	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1805	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1805	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1806	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1806	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1806	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1901	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1901	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1901	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1902	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1902	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1902	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1903	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1903	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1903	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1805	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1805	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2101	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1806	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1806	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2301	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1901	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1901	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1805	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2101	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2201	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1806	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1901	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2401	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2501	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1902	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1903	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1904	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1904	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1904	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1905	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1905	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1905	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1906	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1906	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1906	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1907	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1907	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1907	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1908	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1908	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1908	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1904	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1904	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2503	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1905	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1905	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2505	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1906	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1906	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2506	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1904	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2503	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2504	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1905	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1906	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2506	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2507	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1907	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1908	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1909	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1909	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1909	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1910	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1910	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1910	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1911	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1911	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1911	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1912	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1912	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1912	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1913	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1913	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1913	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1914	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1909	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1909	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2509	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1910	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1910	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2601	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2602	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1911	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1911	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1912	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1912	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2604	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2605	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1913	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1913	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1909	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2509	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1910	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2602	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1911	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2603	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1912	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2605	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1913	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1914	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1914	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1915	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1915	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1915	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
1916	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1916	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
1916	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2001	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2001	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2001	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2101	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2101	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2101	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2201	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
1914	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1914	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2607	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1915	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1915	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2608	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1916	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1916	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2606	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1914	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1915	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2608	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2609	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1916	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2001	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2611	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2101	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2201	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2201	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2301	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2301	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2301	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2401	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2401	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2401	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2501	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2501	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2501	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2502	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2502	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2502	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2503	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2201	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2201	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2613	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2301	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2301	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2614	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2401	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2401	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2615	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2612	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2201	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2301	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2401	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2615	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2616	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2501	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2502	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2503	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2503	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2504	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2504	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2504	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2505	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2505	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2505	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2506	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2506	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2506	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2507	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2507	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2507	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2508	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2508	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2503	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2503	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2618	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2504	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2504	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2620	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2505	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2505	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2621	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2506	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2506	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2623	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2507	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2507	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2619	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2503	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2618	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2619	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2504	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2505	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2621	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2622	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2506	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2507	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2508	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2509	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2509	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2509	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2601	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2601	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2601	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2602	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2602	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2602	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2603	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2603	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2603	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2604	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2604	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2508	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2508	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2624	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2509	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2509	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2626	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2601	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2601	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2508	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2624	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2625	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2509	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2601	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2627	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2628	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2602	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2603	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2604	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2605	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2605	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2605	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2606	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2606	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2606	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2607	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2607	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2607	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2608	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2608	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2608	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2609	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2609	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2604	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2604	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2630	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2605	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2605	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2632	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2606	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2606	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2633	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2634	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2607	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2607	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2635	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2608	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2608	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2604	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2630	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2631	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2605	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2606	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2633	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2607	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2635	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2608	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
201	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2609	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2610	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2610	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2610	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2611	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2611	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2611	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2612	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2612	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2612	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2613	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2613	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2613	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2614	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2609	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2609	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2637	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2610	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2610	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2638	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2611	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2611	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2640	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2636	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2609	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2610	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2638	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2639	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2611	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2612	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2641	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2613	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2643	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2614	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2614	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2615	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2615	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2615	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2616	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2616	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2616	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2617	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2617	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2617	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2618	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2618	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2618	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2619	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2619	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2619	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2614	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2614	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2643	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2615	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2615	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2644	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2616	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2616	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2645	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2617	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2617	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2647	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2618	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2618	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2642	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2614	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2615	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2616	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2645	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2646	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2617	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2618	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2620	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2620	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2620	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2621	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2621	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2621	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2622	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2622	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2622	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2623	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2623	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2623	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2624	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2624	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2624	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2619	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2648	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2620	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2620	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2650	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2621	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2621	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2619	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2648	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2620	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2650	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2651	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2621	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2622	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2653	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2654	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2623	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2625	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2625	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2625	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2626	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2626	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2626	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2627	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2627	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2627	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2628	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2628	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2628	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2629	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2629	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2629	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2624	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2624	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2655	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2625	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2625	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2656	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2626	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2626	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2658	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2624	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2625	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2656	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2657	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2626	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2627	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2659	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2660	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2628	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2630	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2630	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2630	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2631	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2631	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2631	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2632	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2632	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2632	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2633	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2633	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2633	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2634	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2634	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2634	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2629	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2629	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2661	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2630	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2630	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2662	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2631	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2631	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2701	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2629	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2630	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2662	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2631	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2701	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2702	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2632	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2633	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2635	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2635	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2635	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2636	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2636	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2636	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2637	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2637	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2637	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2638	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2638	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2638	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2639	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2639	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2639	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2704	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2635	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2635	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2706	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2636	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2636	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2707	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2637	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2634	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2704	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2705	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2635	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2636	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2707	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2708	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2637	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2638	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2640	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2640	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2640	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2641	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2641	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2641	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2642	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2642	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2642	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2643	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2643	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2643	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2644	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2644	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2644	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2710	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2640	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2640	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2712	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2641	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2641	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2713	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2642	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2639	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2710	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2711	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2640	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2641	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2713	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2714	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2642	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2643	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2645	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2645	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2645	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2646	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2646	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2646	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2647	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2647	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2647	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2648	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2648	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2648	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2649	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2649	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2649	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2645	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2645	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2718	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2646	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2646	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2719	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2647	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2647	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2644	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2716	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2717	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2645	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2646	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2719	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2720	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2647	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2648	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2649	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2650	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2650	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2650	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2651	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2651	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2651	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2652	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2652	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2652	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2653	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2653	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2653	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2654	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2654	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2654	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2650	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2650	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2724	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2651	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2651	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2725	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2652	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2652	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2727	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2722	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2723	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2650	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2651	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2725	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2726	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2652	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2653	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2654	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2655	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2655	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2655	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2656	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2656	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2656	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2657	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2657	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2657	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2658	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2658	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2658	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2659	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2659	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2659	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2728	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2655	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2655	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2730	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2656	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2656	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2731	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2657	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2728	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2729	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2655	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2656	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2731	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2732	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2657	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2658	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2734	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2659	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
201	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2660	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2660	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2660	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2661	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2661	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2661	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2662	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2662	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2662	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2701	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2701	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2701	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2702	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2702	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2702	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2660	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2660	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2736	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2661	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2661	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2737	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2662	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2662	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2739	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2701	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2701	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2702	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2702	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2735	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2660	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2661	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2737	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2738	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2662	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2801	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2701	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2702	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2703	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2703	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2703	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2704	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2704	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2704	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2705	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2705	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2705	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2706	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2706	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2706	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2707	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2707	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2707	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2802	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2703	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2703	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2803	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2704	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2704	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2804	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2705	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2705	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2703	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2803	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2704	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2804	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2805	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2705	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2706	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2807	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2707	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2708	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2708	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2708	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2709	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2709	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2709	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2710	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2710	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2710	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2711	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2711	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2711	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2712	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2712	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2712	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2708	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2708	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2809	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2709	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2709	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2810	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2710	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2808	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2708	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2709	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2810	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2811	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2710	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2711	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2813	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2712	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2713	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2713	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2713	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2714	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2714	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2714	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2715	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2715	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2715	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2716	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2716	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2716	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2717	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2717	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2717	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2713	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2713	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2814	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2714	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2714	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2816	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2715	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2715	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2713	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2814	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2815	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2714	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2715	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2817	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2818	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2716	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2717	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2718	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2718	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2718	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2719	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2719	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2719	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2720	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2720	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2720	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2721	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2721	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2721	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2722	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2722	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2722	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2718	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2718	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2820	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2719	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2719	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2822	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2720	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2720	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2823	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2721	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2718	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2820	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2821	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2719	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2720	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2823	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2824	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2721	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2722	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2723	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2723	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2723	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2724	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2724	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2724	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2725	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2725	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2725	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2726	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2726	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2726	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2727	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2727	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2727	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2723	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2723	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2826	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2724	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2724	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2828	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2725	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2725	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2723	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2826	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2827	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2724	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2725	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2829	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2830	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2726	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2727	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2728	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2728	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2728	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2729	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2729	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2729	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2730	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2730	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2730	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2731	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2731	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2731	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2732	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2732	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2732	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2728	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2728	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2832	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2729	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2729	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2834	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2730	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2730	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2728	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2832	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2833	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2729	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2730	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2835	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2836	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2731	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2732	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2733	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2733	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2733	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2734	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2734	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2734	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2735	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2735	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2735	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2736	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2736	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2736	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2737	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2737	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2737	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2733	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2733	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2838	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2734	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2734	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2840	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2735	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2735	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2901	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2733	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2838	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2839	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2734	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2735	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2901	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3001	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2736	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2737	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2738	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2738	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2738	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2739	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2739	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2739	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2801	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2801	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2801	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2802	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2802	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2802	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2803	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2803	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2803	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2804	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2738	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2738	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3102	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2739	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2739	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3104	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2801	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2801	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3105	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2802	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2802	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3107	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2803	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2803	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3108	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2738	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3102	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3103	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2739	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2801	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3105	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3106	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2802	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2803	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2805	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2804	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2804	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2805	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2805	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2805	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2806	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2806	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2806	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2807	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2807	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2807	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2808	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2808	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2808	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2809	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2804	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2804	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3201	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2805	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2805	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2806	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2806	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3302	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2807	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2807	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3303	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2808	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2808	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3108	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2804	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3201	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2805	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3202	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3301	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2806	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2807	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3303	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3304	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2808	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2809	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2809	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2810	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2810	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2810	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2811	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2811	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2811	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2812	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2812	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2812	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2813	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2813	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2813	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2809	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2809	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3305	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2810	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2810	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3306	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2811	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2811	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3308	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2812	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2812	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2809	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2810	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3306	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3307	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2811	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2812	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3309	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3310	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2813	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3312	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2814	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2814	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2814	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2815	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2815	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2815	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2816	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2816	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2816	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2817	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2817	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2817	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2818	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2818	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2818	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2819	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2819	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2814	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2814	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3311	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3312	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2815	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2815	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3313	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2816	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2816	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3314	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2817	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2817	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3316	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2818	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2818	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2814	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3312	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2815	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2816	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3314	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3315	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2817	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2818	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2819	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2820	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2820	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2820	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2821	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2821	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2821	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2822	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2822	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2822	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2823	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2823	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2823	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2824	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2819	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2819	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3317	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2820	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2820	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3319	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2821	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2821	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3320	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2819	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3317	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3318	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2820	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2821	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3320	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3321	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2822	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2823	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
201	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2824	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2824	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2825	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2825	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2825	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2826	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2826	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2826	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2827	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2827	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2827	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2828	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2828	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2828	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2829	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2824	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2824	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3323	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2825	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2825	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3325	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2826	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2824	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3323	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3324	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2825	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2826	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3326	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2827	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3328	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2828	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2829	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2829	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2830	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2830	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2830	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2831	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2831	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2831	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2832	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2832	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2832	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2833	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2833	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2833	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2834	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2834	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2829	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2829	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3402	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2830	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2830	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3403	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2831	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2831	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3405	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2832	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2832	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3406	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2833	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2833	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3401	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2829	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2830	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3403	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3404	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2831	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2832	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3406	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3407	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2833	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2834	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2835	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2835	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2835	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2836	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2836	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2836	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2837	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2837	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2837	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2838	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2838	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2838	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2839	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2834	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2834	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3408	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2835	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2835	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3409	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2836	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2836	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3412	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2837	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2837	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2834	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2835	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3409	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3410	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2836	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3412	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2837	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2838	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2839	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2839	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
2840	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2840	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2840	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3702	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2901	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2901	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
2901	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3702	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3702	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3001	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3001	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3001	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3101	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3101	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3101	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3102	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
2839	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2839	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3601	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2840	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2840	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2901	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2901	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3702	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3001	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3001	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3702	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3702	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3101	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3101	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2839	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3601	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3701	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2840	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
2901	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3001	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3702	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3101	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3102	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3102	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3103	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3103	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3103	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3703	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3703	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3104	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3104	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3104	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3703	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3105	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3105	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3105	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3106	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3106	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3106	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3107	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3107	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3107	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3102	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3102	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3103	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3103	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3104	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3104	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3703	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3703	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3105	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3105	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3703	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3106	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3106	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3102	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3103	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3104	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3105	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3703	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3703	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3106	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3108	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3108	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3108	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3704	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3704	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3704	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3201	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3201	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3201	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3202	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3202	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3202	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3301	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3301	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3301	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3302	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3302	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3302	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3107	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3108	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3108	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3201	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3201	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3704	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3704	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3202	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3202	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3301	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3301	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3107	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3108	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3201	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3704	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3704	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3202	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3301	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3705	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3303	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3303	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3303	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3705	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3705	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3304	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3304	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3304	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3305	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3305	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3305	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3306	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3306	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3306	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3307	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3307	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3307	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3302	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3302	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3303	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3303	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3705	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3304	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3304	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3705	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3705	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3305	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3305	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3706	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3306	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3306	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3307	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3307	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3302	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3303	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3304	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3705	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3305	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3706	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3306	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3308	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3707	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3707	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3308	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3308	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3308	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3707	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3309	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3309	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3309	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3310	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3310	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3310	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3311	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3311	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3311	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3312	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3312	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3312	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3308	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3308	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3707	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3707	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3309	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3309	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3310	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3310	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3311	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3311	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3312	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3312	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3307	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3308	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3707	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3309	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3707	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3310	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3311	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3708	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3708	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3313	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3313	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3313	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3708	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3708	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3314	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3314	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3314	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3315	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3315	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3315	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3316	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3316	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3316	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3317	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3317	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3317	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3313	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3313	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3708	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3314	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3314	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3708	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3708	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3315	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3315	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3316	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3316	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3317	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3317	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3312	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3313	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3314	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3708	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3315	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3316	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3318	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3318	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3318	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3801	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3801	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3801	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3319	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3319	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3319	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3320	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3320	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3320	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3321	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3321	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3321	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3322	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3322	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3322	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3318	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3318	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3319	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3319	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3801	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3801	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3801	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3320	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3320	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3321	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3321	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3322	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3322	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3317	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3318	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3319	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3801	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3320	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3321	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3322	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3901	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3323	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3323	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3323	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3901	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3901	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3324	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3324	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3324	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3325	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3325	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3325	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3326	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3326	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3326	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3327	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3327	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3327	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3323	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3323	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3901	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3324	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3324	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3901	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3901	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3325	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3325	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3326	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3326	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3327	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3327	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3323	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3324	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3901	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3901	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3325	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3902	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3326	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3327	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3328	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3328	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3328	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3903	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3903	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3903	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3401	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3401	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3401	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3402	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3402	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3402	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3403	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3403	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3403	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3904	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3404	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3404	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3404	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3328	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3328	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3401	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3401	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3903	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3903	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3903	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3402	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3402	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3403	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3403	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3404	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3404	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3328	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3401	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3903	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3402	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3403	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3404	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3405	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3405	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3405	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3904	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3904	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3406	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3406	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3406	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3407	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3407	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3407	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3408	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3408	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3408	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3905	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3905	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3409	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3409	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3409	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3405	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3405	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3904	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3406	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3406	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3904	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3904	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3407	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3407	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3408	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3408	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3409	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3409	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3405	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3406	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3904	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3904	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3407	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3408	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3409	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3410	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
301	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3410	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3410	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3905	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3411	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3411	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3411	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3412	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3412	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3412	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3501	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3501	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3501	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3906	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3601	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3601	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3601	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3410	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3410	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3905	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3411	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3411	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3905	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3412	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3412	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3501	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3501	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3601	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3601	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3410	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3411	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3905	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3905	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3412	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3501	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3601	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3701	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3701	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3701	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
301	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
301	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
301	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
302	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
302	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3701	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
302	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3701	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
303	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
303	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
303	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
303	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
304	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
304	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
304	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
304	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
305	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3701	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
305	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
305	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
306	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
307	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
306	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
306	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
306	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
307	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
307	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
307	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
309	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
308	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
308	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
308	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
309	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
309	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
309	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
501	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
502	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
401	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
401	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
401	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
501	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
501	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
501	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
504	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
505	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
502	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
502	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
502	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
503	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
503	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
503	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
504	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
504	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
504	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
507	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3706	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3706	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3706	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
508	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
505	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
505	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
505	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
506	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
506	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3706	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
506	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3706	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3706	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
510	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
511	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
507	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
507	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
507	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
601	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
508	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
508	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
508	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
509	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
509	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
509	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
510	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
510	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
510	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
603	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
604	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
511	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
511	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
511	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
512	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
512	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
512	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
606	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
607	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
601	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
601	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
601	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
602	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
602	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
602	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
609	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
603	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
603	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
603	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
610	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
604	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
604	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
604	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
801	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
605	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
605	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
605	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
802	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
606	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
606	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
606	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3902	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3902	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3902	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
607	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
607	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
607	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
608	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
608	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3902	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
608	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3902	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
804	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
609	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
609	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
609	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
805	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
610	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
610	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
610	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
807	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3902	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
701	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
701	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
701	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
808	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
801	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
801	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
801	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
901	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
802	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
802	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
802	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
803	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
803	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
803	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1002	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1003	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
804	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
804	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
804	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
805	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
805	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
805	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
806	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1002	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
806	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
806	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1005	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1006	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
807	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
807	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
807	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1008	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
808	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
808	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
808	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1101	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
901	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
901	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
901	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1201	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1001	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1001	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1001	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1002	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1002	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1203	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1003	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1003	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1003	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1204	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1004	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1004	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1004	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1206	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1005	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1005	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1005	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1207	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1006	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1006	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1006	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1007	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1007	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1008	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1008	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1008	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1209	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1210	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1101	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3906	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3906	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1101	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1101	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1212	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1201	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1201	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1201	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1213	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1202	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1202	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1202	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1203	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1203	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1203	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1215	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1204	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1204	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1204	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1216	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1205	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1205	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1205	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1218	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3906	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3906	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3907	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3907	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3906	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3906	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1206	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1206	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1206	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1219	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1207	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1207	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1207	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1208	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1208	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1208	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1221	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1209	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1209	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1209	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1222	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1210	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1210	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1210	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1401	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1211	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1211	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1211	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1501	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1212	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1212	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1212	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1213	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1213	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1213	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1701	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1214	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1214	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1214	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1801	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1215	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1215	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1215	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1803	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1216	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1216	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1216	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1804	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1217	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1217	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1217	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1218	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1218	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1218	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1806	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1219	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1219	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1219	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1902	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3907	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3908	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3908	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3908	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3907	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3907	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3907	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4002	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1220	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1220	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1220	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1903	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1221	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1221	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1221	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1905	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1222	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1222	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1222	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1223	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1223	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1223	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1906	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1301	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1301	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1301	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1908	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1401	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1401	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1401	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1909	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1501	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1501	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1501	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1911	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1601	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1601	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1601	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1701	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1701	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1701	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1912	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1801	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1801	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1801	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1914	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1802	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1802	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1802	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1915	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1803	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1803	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1803	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2001	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1804	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1804	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1804	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1805	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1805	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1805	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2101	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3909	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3909	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
3909	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
3909	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3908	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3908	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3909	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3908	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
1806	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1806	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1806	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2301	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
302	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1901	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1901	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1901	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2401	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1902	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1902	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1902	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2502	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1903	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1903	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1903	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2503	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1904	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1904	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1904	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1905	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1905	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1905	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2505	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2506	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1906	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1906	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1906	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1907	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1907	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1907	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2508	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2509	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1908	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1908	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1908	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1909	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1909	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1909	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2601	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1910	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1910	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1910	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2602	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1911	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1911	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1911	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
305	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
308	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
401	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
503	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
506	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
201	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
303	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
306	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
309	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
501	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
504	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2604	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1912	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1912	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1912	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2605	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1913	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1913	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1913	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1914	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1914	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1914	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2607	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1915	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1915	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1915	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2608	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1916	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1916	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1916	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2610	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2001	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2001	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2001	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2611	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2101	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2101	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2101	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2201	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2201	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2201	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2613	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2301	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2301	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2301	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2614	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2615	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2401	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2401	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2401	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
507	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2501	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2501	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2501	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2617	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
509	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
512	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
602	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
605	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
608	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
701	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
803	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
510	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
510	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
603	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
606	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
609	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
801	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
804	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2618	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2502	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2502	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2502	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2503	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2503	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2503	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2504	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2504	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2504	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2620	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2621	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2505	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2505	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2505	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2506	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2506	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2506	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2623	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2624	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2507	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2507	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2507	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2508	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2508	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2508	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2509	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2509	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2509	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2626	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2627	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2601	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2601	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2601	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2602	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2602	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2602	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2629	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2630	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2603	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2603	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2603	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2604	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2604	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2604	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2605	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
806	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1001	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1004	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1007	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1202	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1205	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
807	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1002	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1005	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1008	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1101	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1203	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1206	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2605	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2605	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2632	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2633	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2606	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2606	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2606	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2607	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2607	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2607	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2635	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2608	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2608	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2608	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2609	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2609	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2609	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2637	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2610	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2610	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2610	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2638	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2611	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2611	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2611	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2640	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2612	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2612	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2612	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2641	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2613	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2613	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2613	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2614	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2614	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2614	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2615	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2615	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
1209	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2615	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2644	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2645	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2616	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
1208	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1211	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1214	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1217	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1220	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1223	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1301	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1601	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1209	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1212	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1215	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1218	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1221	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1401	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2616	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2616	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2617	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2617	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2617	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2647	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2648	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2618	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2618	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2618	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2619	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2619	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2619	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2650	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2620	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2620	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2620	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2621	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2621	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2621	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2652	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2653	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2622	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2622	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2622	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2623	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2623	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2623	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2624	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2624	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2624	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2655	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2656	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2625	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2625	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2625	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2626	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2626	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2626	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2658	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2659	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2627	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2627	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2627	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
1802	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1805	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1901	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1904	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1907	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1910	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1913	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1701	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1803	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1806	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1902	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1905	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1908	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
1911	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2628	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2628	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2628	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2629	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2629	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2629	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2661	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2662	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2630	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2630	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2630	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2701	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2631	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2631	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2631	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2632	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2632	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2632	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2703	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2704	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2633	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2633	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2633	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2634	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2634	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2634	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2635	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2635	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2635	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2706	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2636	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2636	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2636	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2707	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2637	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2637	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2637	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2709	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2638	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2638	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2638	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2639	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2639	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2639	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2710	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1916	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2201	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2501	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2504	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2507	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2603	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1914	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2001	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2301	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2502	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2505	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2508	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2601	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2604	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2640	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2640	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2640	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2712	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2641	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2641	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2641	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2713	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2642	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2642	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2642	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2715	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2643	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2643	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2643	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2716	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2644	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2644	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2644	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2645	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2645	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2645	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2718	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2719	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2646	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2646	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2646	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2647	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2647	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2647	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2721	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2722	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2648	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2648	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2648	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2649	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2649	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2649	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2650	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2650	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2650	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2724	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2651	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2651	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2606	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2609	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2612	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2616	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2619	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2622	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2625	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2607	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2610	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2613	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2614	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2617	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2620	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2623	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2651	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2725	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2652	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2652	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2652	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2727	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2653	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2653	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2653	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2728	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2654	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2654	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2654	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2655	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2655	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2655	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2730	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2656	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2656	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2656	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2731	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2657	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2657	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2657	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2733	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2658	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2658	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2658	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2734	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2659	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2659	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2659	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2660	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2660	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2660	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2736	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2661	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2661	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2661	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2737	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2662	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2662	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2662	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2739	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2628	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2631	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2636	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2639	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2642	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2646	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2626	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2629	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2632	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2634	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2637	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2640	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2643	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2644	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2701	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2701	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2701	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2702	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2702	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2702	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2802	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2703	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2703	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2703	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2803	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2704	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2704	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2704	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2705	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2705	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2705	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2806	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2807	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2706	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2706	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2706	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2707	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2707	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2707	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2708	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2708	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2708	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2809	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2709	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2709	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2709	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2810	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2710	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2710	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2710	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2812	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2711	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2711	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2711	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2813	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2712	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2712	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2712	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2738	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2649	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2651	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2654	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2657	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2660	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2702	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2647	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2649	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2652	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2655	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2658	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2661	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2703	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2713	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2713	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2713	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2714	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2714	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2714	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2816	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2817	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2715	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2715	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2715	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2716	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2716	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2716	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2819	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2820	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2717	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2717	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2717	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2718	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2718	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2718	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2719	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2719	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2719	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2822	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2823	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2720	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2720	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2720	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2721	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2721	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2721	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2825	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2826	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2722	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2722	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2722	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2723	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2723	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2723	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2724	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2724	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2724	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2828	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2705	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2708	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2711	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2714	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2717	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2720	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2723	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2706	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2709	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2712	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2715	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2718	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2721	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2724	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2829	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2725	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2725	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2725	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2726	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2726	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2726	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2831	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2832	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2727	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2727	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2727	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2728	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2728	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2728	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2729	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2729	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2729	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2834	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2835	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2730	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2730	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2730	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2731	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2731	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2731	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2837	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2838	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2732	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2732	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2732	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2733	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2733	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2733	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2734	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2734	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2734	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2840	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2901	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2735	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2735	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2735	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2736	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2726	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2729	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2732	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2735	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2738	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2801	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2805	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2727	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2730	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2733	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2736	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2739	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2802	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2806	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2736	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2736	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3101	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3102	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2737	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2737	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2737	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2738	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2738	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2739	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2739	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2739	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3104	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3105	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2801	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2801	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2801	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2802	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2802	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2802	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3107	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3108	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2803	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2803	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2803	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2804	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2804	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2804	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3201	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2805	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2805	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2806	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2806	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2806	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3302	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3303	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2807	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2807	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2807	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2808	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2808	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2808	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2809	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2809	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2808	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2811	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2815	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2818	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2821	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2824	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2827	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2809	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2812	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2816	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2819	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2822	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2825	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2809	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3305	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3306	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2810	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2810	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2810	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2811	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2811	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2811	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3308	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3309	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2812	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2812	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2812	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2813	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2813	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2813	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3311	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2814	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2814	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2814	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2815	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2815	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2815	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3313	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2816	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2816	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2816	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3314	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2817	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2817	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2817	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3316	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2818	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2818	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2818	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2819	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2819	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2819	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3317	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2820	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2820	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2820	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3319	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2830	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2833	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2836	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2839	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3001	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3103	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3106	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2828	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2831	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2834	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2837	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2840	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3101	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3104	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2821	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2821	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2821	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3320	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2822	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2822	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2822	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3322	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2823	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2823	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2823	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2824	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2824	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2824	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3323	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2825	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2825	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2825	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3325	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2826	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2826	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2826	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3326	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2827	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2827	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2827	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3328	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2828	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2828	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2828	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2829	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2829	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2829	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3402	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2830	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2830	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2830	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3403	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2831	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2831	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2831	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3405	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2832	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2832	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2832	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3406	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3202	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3301	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3304	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3307	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3310	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3315	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3107	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3302	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3305	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3308	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3311	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3313	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3316	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
2833	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2833	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2833	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2834	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2834	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2834	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3408	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3409	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2835	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2835	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2835	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2836	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2836	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2836	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3411	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3412	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2837	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2837	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2837	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3501	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2838	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2838	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2838	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
2839	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2839	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2839	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3601	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2840	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2840	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2840	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3702	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2901	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
2901	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
2901	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3001	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3001	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3001	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3101	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3101	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3101	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3102	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3102	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3102	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3703	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3318	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3321	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3324	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3327	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3401	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3404	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3407	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3319	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3322	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3325	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3327	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3402	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3405	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3408	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3703	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3703	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3103	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3103	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3103	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3703	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3104	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3104	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3104	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3105	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3105	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3105	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3106	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3106	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3106	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3107	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3107	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3107	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3704	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3704	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3704	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3108	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3108	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3108	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3201	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3201	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3201	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3202	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3202	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3202	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3705	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3705	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3301	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3301	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3301	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3302	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3302	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3302	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3705	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3705	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3411	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3303	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3303	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3303	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3304	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3410	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3701	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3704	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3707	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3902	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3905	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3411	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3501	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3702	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3705	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3708	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3801	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3903	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3906	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3304	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3304	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3305	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3305	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3305	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3706	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3306	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3306	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3306	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3707	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3307	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3307	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3307	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3308	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3308	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3309	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3309	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3309	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3708	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3310	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3310	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3310	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3708	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3708	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3311	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3909	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3910	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4005	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3910	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4008	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3311	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3311	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3312	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3312	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3312	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3313	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3313	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3313	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3314	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3910	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3908	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4001	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4004	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4007	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4010	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3907	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3909	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3909	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4002	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4011	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3907	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3908	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3909	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4001	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4001	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4001	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4002	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4002	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4002	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4003	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4003	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4003	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4004	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4004	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4004	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4005	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4005	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4005	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
3910	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
3910	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3910	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4001	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4001	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4002	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4002	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4003	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4003	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4003	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4004	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4004	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4005	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4005	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4005	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
3910	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
3910	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4001	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4001	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4002	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4003	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4003	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4004	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4004	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4006	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4006	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4006	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4007	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4007	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4007	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4008	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4008	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4008	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4009	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4009	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4009	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4010	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4010	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4010	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4006	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4006	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4006	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4007	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4007	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4008	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4008	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4008	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4009	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4009	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4005	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4006	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4006	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4007	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4007	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4008	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4009	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4009	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
4010	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4010	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3314	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3314	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3315	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3315	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3315	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3801	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3801	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3316	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3316	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3316	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3317	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3317	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3317	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3801	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3801	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3318	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3318	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3318	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3319	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3319	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3319	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3320	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3320	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3320	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3901	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3901	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3321	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3321	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3321	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3901	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3901	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3322	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3322	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3322	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3323	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3323	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3323	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3324	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3324	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3324	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3325	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3325	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3325	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4011	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4011	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4011	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4012	5	VIP 2 (Cloudflare R2 - Video.mp4)	1	4K HDR	r2://webxemphim-movies/Video.mp4
4012	6	VIP 2 (Cloudflare R2 - Video.mp4)	1	1080p FHD	r2://webxemphim-movies/Video.mp4
4012	7	VIP 2 (Cloudflare R2 - Video.mp4)	1	720p HD	r2://webxemphim-movies/Video.mp4
4011	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4011	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4011	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4012	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4012	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4012	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4011	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4012	11	Direct Backup (R2 Video.mp4)	3	720p HD	r2://webxemphim-movies/Video.mp4
4012	12	Direct Backup (R2 Video.mp4)	3	480p SD	r2://webxemphim-movies/Video.mp4
3902	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3326	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3326	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3326	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3327	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3327	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3327	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3903	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3328	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3328	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3328	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3401	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3401	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3401	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3402	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3402	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3402	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3904	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3904	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3403	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3403	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3403	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3904	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3904	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3404	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3404	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3404	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3405	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3405	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3405	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3406	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3406	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3406	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3407	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3407	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3407	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3905	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3905	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3408	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3408	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3408	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3905	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3409	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3409	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3409	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3410	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3410	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3410	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3701	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3411	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3411	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3411	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3412	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3412	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3412	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3906	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3906	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3906	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3501	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3501	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3501	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3906	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3601	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3601	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3601	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3701	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3701	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3702	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3702	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3702	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3706	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3706	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3706	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3707	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3707	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3902	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3902	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3903	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3903	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3903	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3907	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3907	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3907	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3907	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3908	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3908	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3908	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
3909	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3909	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
101	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
302	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
305	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
308	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
401	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
503	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
506	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
509	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
512	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
602	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
605	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
608	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
701	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
803	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
806	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1001	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1004	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1007	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1202	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1205	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1208	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1211	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1214	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1217	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1220	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1223	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1301	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1601	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1802	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1805	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1901	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1904	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1907	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1910	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1913	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
1916	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2201	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2501	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2504	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2507	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2603	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2606	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2609	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2612	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2616	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2619	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2622	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2625	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2628	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2631	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2634	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2636	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2639	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2642	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2646	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2649	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2651	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2654	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2657	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2660	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2702	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2705	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2708	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2711	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2714	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2717	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2720	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2723	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2726	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2729	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2732	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2735	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2738	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2801	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2804	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2805	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2808	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2811	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2814	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2815	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2818	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2821	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2824	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2827	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2830	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2833	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2836	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
2839	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3001	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3103	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3106	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3202	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3301	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3304	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3307	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3310	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3315	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3318	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3321	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3324	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3327	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3401	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3404	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3407	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3410	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3701	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3704	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3707	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3902	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3905	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3908	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4001	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4004	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4007	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4010	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3909	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3910	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
3910	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
3910	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
3910	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4001	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4001	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4001	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4002	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4002	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4002	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4002	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4003	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4003	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4003	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4003	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4004	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4004	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4004	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4005	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4005	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4005	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4005	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4006	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4006	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4006	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4006	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4007	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4007	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4007	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4008	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4008	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4008	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4008	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4009	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4009	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4009	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4009	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4010	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4010	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4010	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4011	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4011	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4011	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4011	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
4012	1	VIP 1 (Cloudflare R2 - play.mp4)	1	4K Ultra HD	r2://webxemphim-movies/play.mp4
4012	2	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p 60fps	r2://webxemphim-movies/play.mp4
4012	3	VIP 1 (Cloudflare R2 - play.mp4)	1	1080p FHD	r2://webxemphim-movies/play.mp4
4012	4	VIP 1 (Cloudflare R2 - play.mp4)	1	720p HD	r2://webxemphim-movies/play.mp4
307	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
307	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
307	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
309	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
308	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
308	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
504	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
502	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
502	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
505	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
503	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
503	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
507	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
508	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
508	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
512	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
512	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
606	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
601	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
601	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
604	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
605	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
605	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
801	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
802	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
606	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
606	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
610	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
807	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
701	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
701	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
901	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
901	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1201	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1001	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1001	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1206	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1005	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1005	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1207	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1006	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1006	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1201	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1212	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1213	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1202	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1202	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1218	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1206	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1206	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1219	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1207	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1207	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1211	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1401	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1501	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1212	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1212	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1221	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1905	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1222	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1222	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1501	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1501	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1911	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1601	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1601	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1915	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1803	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1803	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2001	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1804	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1804	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2401	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1902	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1902	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2502	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1903	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1903	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
1907	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1907	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2508	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
1908	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
1908	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2610	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2001	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2001	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2611	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2101	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2101	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2501	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2501	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2617	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2502	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2502	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2627	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2602	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2602	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2629	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2603	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2603	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2612	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2612	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2641	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2613	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2613	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2652	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2622	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2622	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2653	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2623	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2623	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2627	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2627	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2659	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2628	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2628	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2632	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2632	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2703	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2633	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2633	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2634	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2634	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2637	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2709	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2638	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2638	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2639	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2639	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2642	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2715	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2643	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2643	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2716	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2644	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2644	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2721	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2648	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2648	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2722	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2649	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2649	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2653	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2653	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2654	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2654	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2657	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2733	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2658	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2658	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2734	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2659	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2659	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2806	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2706	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2706	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2807	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2707	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2707	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2710	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2812	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2711	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2711	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2813	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2712	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2712	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2817	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2716	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2716	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2819	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2717	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2717	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2721	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2825	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2722	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2722	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2829	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2726	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2726	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2831	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2727	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2727	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2835	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2731	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2731	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2837	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2732	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2732	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3107	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2736	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2736	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3101	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2737	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2737	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3309	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2813	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2813	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2822	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2822	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3322	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2823	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2823	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
2826	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3326	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2827	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2827	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3328	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2828	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2828	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
3501	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
2838	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
2838	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
4009	10	Hydra CDN (R2 play.mp4)	2	480p SD	r2://webxemphim-movies/play.mp4
4010	8	Hydra CDN (R2 play.mp4)	2	1080p FHD	r2://webxemphim-movies/play.mp4
4010	9	Hydra CDN (R2 play.mp4)	2	720p HD	r2://webxemphim-movies/play.mp4
\.


--
-- Data for Name: user_library; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.user_library ("UserId", "MovieId", "AddedAt") FROM stdin;
\.


--
-- Data for Name: user_security; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.user_security ("UserId", "IsAdmin", "PasswordIterations", "FailedLoginAttempts", "LockedUntilUtc", "Version") FROM stdin;
11111111-1111-1111-1111-111111111111	t	600000	0	\N	7
\.


--
-- Data for Name: user_settings; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.user_settings ("UserId", "DefaultQuality", "AutoplayNext", "NotifyNewEpisodes", "Language", "UpdatedAtUtc") FROM stdin;
11111111-1111-1111-1111-111111111111	4K khi có thể	t	t	vi	2026-11-03 13:52:07.597434+00
\.


--
-- Data for Name: users; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.users ("Id", "FullName", "Email", "NormalizedEmail", "EmailLookupHash", "PasswordSalt", "PasswordHash", "Membership", "VipExpiresAtUtc", "CreatedAtUtc") FROM stdin;
22222222-2222-2222-2222-222222222222	CfDJ8I9_-lw2h3xDmM17PtsDvrVaRTRQxrk9eZy6MWKgSiRy_BHXcY6oAgrGIspQ7y5S1tN0f2SfA1yPWKO9bkm_ADDLCEV_nGxvU9QYFaLtf-HGBMHdkaAfvqZIRCba5HnQvyIk0BsxIB813vwCFcMnUqs	CfDJ8I9_-lw2h3xDmM17PtsDvrW8dd7kj8F7ufmLMIm4krMbZrINedBy9a-iCv67uNXh_QApmLrGGW5_EIiqG11srqMUkpbZebxdjQ56jtXU1xqgN_jOK9lLVM-rtEL-NrXusyn5ErbSeqIGspndcPEqRAc	hoangnam@cinemaflow.vn	ebb3a3790fcda7500bb569056199fb40b9f548f06c97d8f9f3cea967bca06766	\\x13021091d1dc26ede5c01dcac45f45fa	\\x76e49aeb854c3e940d258cde196924be992aacb7a5edf8f6dc8f650000e0694c	2	2027-02-01 13:52:07.597434+00	2026-08-03 13:52:07.597434+00
33333333-3333-3333-3333-333333333333	CfDJ8I9_-lw2h3xDmM17PtsDvrWYNQtKZQsKx62ibnP29moGB-H_MEPGLxteooP1mFQ3HYzNS5kh4BgT8HcVgkOuDsBoHWCudM2LCZy_ib_U8zaNZdC9VjgHHN94svUrsbuAig	CfDJ8I9_-lw2h3xDmM17PtsDvrUxsNMsJTpeo2coJC8Al0U5c69ppRFElpR5rqgt3gBpRqCCG32ikcgFn9pDdG0LFJ1v_3O-i1wKvwHBdMAnJGzQab0jSIiMC36nqpFpkXWOeaGS7J-0ujSGCBsa2l70PkI	vip@cinemaflow.vn	47929ca42709c0543d8deb95fd2bf8824809e70c71c823c4297fce4c2fe79af5	\\xd3549a854a2c137e7390b09e4a6b9d56	\\x913f52c8fccb3fb35d3ca04a4176839eb76fd2ebc6bf3800df79a02ce14ee89d	2	2026-12-03 13:52:07.597434+00	2026-10-19 13:52:07.597434+00
44444444-4444-4444-4444-444444444444	CfDJ8I9_-lw2h3xDmM17PtsDvrUgqYwSRdTb4dn0QAcYaEpKU_4bKfTp34y0zf6QYhnvYuKHgUaxl3nJxxKqqDKfVdPoxUr05oqzgXYBvRwinVahCiE89mKha0QRJmZ8ALbsNfL7sIOgFYfH3JVu2eJtiPw	CfDJ8I9_-lw2h3xDmM17PtsDvrXLZJ9JBBY7SsUyv6OyHbvhDhNTOn9NdCjNb3_GGRMS8_VS1G26K72F_DZ4gOhaiIfVcKAbx0VjAJFaYzn7xaynAb3F7Y0-CxFFOnQFiEfwDpDrMtF-G_89fsFCQ0ACNZw	thuylinh@cinemaflow.vn	851068cc8059491e0351caf13a56a72c86f60dad85922477ef6d5f90a32a131f	\\xa9740db6cfa11e4ae4ead4ede34acd88	\\x047f0106f6b597d63e93e28bc49a4b0e87de6bdcf47af789dd65cfacb6f932cf	1	\N	2026-10-03 13:52:07.597434+00
55555555-5555-5555-5555-555555555555	CfDJ8I9_-lw2h3xDmM17PtsDvrW0qDrqmczaCm1O1NNlJDKqQznojmk9YvN85FQ_Poti4VByjZ2-D1lQ9nOSd_aw9LYBxZNuIERahMzlEbl5oJG_lddwfUyjzM0hSxYzNVUK0A	CfDJ8I9_-lw2h3xDmM17PtsDvrXrlAuhIAcojE4ZC01eDyh-DYhy5jPzJdAG8BVfc7QZBMmZuCOwyBys5L6W_S59JEz1YB09Vl1BTl_C8KbnN0bkvMS-c1q9e47m9wpFBH-VaquGc3jTIkTuGmaKPPv0mUQ	longle@cinemaflow.vn	ad1ab7a80febfe91970d1657791714256b407d9faf12fc098a6f6a246a4412c6	\\x80505e2de3c4bb8d4e2add560cd1324c	\\x7b9eb52a860c4e5997710e0b21470302021fbc2dd363bb79fae22df5963b870c	1	\N	2026-10-14 13:52:07.597434+00
11111111-1111-1111-1111-111111111111	CfDJ8I9_-lw2h3xDmM17PtsDvrXx1PQ9fZRlUTaEl36y8K0kcfEOcqEljWES6saUrN0WC7fw7U7RAbxUSI-AANky74kwiii2XPV0SV6eDhuZ_EsmRyedzIndI1pOgJOtdS0zBboSxfpl4lqJuI3y1LkGIK8	CfDJ8I9_-lw2h3xDmM17PtsDvrX5mqtWBGLNA9wteEphqOxHbZFNli__PLJVGZQlMqgyTznC-b0eMZNc2uB2GhajuQIXe2vKBr0Ep0rbQzHF-1BJrCIzwQt_FmE1ZCXhB23yjv2l2fJPOcyHhWJfNL9A19Q	admin@cinemaflow.vn	f03c643bdf49aaf8eb8d25bdfcad4170b72b2556f1670aeecc444f6b15cac83b	\\x678dcf22c5180321b17013f3dbfcbc36	\\xc82941dc28c8b03ca707347dfbf35fce4b7cca807e6305551cefe82fb8bb556d	2	2027-11-03 13:52:07.597434+00	2026-05-03 13:52:07.597434+00
\.


--
-- Data for Name: vip_plans; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.vip_plans ("Id", "Name", "Description", "PriceVnd", "DurationDays", "IsActive", "DisplayOrder") FROM stdin;
vip-180	Sáu tháng	Bán chạy nhất cho người luyện phim - 180 ngày đặc quyền VIP	349000.00	180	t	3
vip-30	Một tháng	Linh hoạt, dễ bắt đầu - 30 ngày trải nghiệm xem phim không giới hạn	79000.00	30	t	1
vip-365	Một năm	Lựa chọn tốt nhất cho người yêu phim - 365 ngày đặc quyền tối thượng	499000.00	365	t	4
vip-90	Ba tháng	Tiết kiệm hơn, trải nghiệm dài hơn - 90 ngày xem phim chất lượng cao	199000.00	90	t	2
\.


--
-- Data for Name: watch_progress; Type: TABLE DATA; Schema: webxemphim; Owner: -
--

COPY webxemphim.watch_progress ("UserId", "MovieId", "EpisodeId", "EpisodeNumber", "ProgressSeconds", "DurationSeconds", "LastWatchedAtUtc") FROM stdin;
11111111-1111-1111-1111-111111111111	2	\N	1	3100	10800	2026-11-03 08:52:07.597434+00
11111111-1111-1111-1111-111111111111	1	101	1	3	10080	2026-11-04 04:31:05.788908+00
\.


--
-- Name: episodes_Id_seq; Type: SEQUENCE SET; Schema: webxemphim; Owner: -
--

SELECT pg_catalog.setval('webxemphim."episodes_Id_seq"', 4012, true);


--
-- Name: genres_Id_seq; Type: SEQUENCE SET; Schema: webxemphim; Owner: -
--

SELECT pg_catalog.setval('webxemphim."genres_Id_seq"', 15, true);


--
-- Name: movies_Id_seq; Type: SEQUENCE SET; Schema: webxemphim; Owner: -
--

SELECT pg_catalog.setval('webxemphim."movies_Id_seq"', 40, true);


--
-- Name: playback_telemetry_Id_seq; Type: SEQUENCE SET; Schema: webxemphim; Owner: -
--

SELECT pg_catalog.setval('webxemphim."playback_telemetry_Id_seq"', 5, true);


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: access_tokens PK_access_tokens; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.access_tokens
    ADD CONSTRAINT "PK_access_tokens" PRIMARY KEY ("Token");


--
-- Name: active_streams PK_active_streams; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.active_streams
    ADD CONSTRAINT "PK_active_streams" PRIMARY KEY ("Id");


--
-- Name: app_maintenance_flags PK_app_maintenance_flags; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.app_maintenance_flags
    ADD CONSTRAINT "PK_app_maintenance_flags" PRIMARY KEY ("Flag");


--
-- Name: audit_logs PK_audit_logs; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.audit_logs
    ADD CONSTRAINT "PK_audit_logs" PRIMARY KEY ("Id");


--
-- Name: cast_members PK_cast_members; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.cast_members
    ADD CONSTRAINT "PK_cast_members" PRIMARY KEY ("MovieId", "Position");


--
-- Name: coupons PK_coupons; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.coupons
    ADD CONSTRAINT "PK_coupons" PRIMARY KEY ("Code");


--
-- Name: episodes PK_episodes; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.episodes
    ADD CONSTRAINT "PK_episodes" PRIMARY KEY ("Id");


--
-- Name: genres PK_genres; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.genres
    ADD CONSTRAINT "PK_genres" PRIMARY KEY ("Id");


--
-- Name: mfa_challenges PK_mfa_challenges; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.mfa_challenges
    ADD CONSTRAINT "PK_mfa_challenges" PRIMARY KEY ("TokenHash");


--
-- Name: mfa_credentials PK_mfa_credentials; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.mfa_credentials
    ADD CONSTRAINT "PK_mfa_credentials" PRIMARY KEY ("UserId");


--
-- Name: mfa_verified_sessions PK_mfa_verified_sessions; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.mfa_verified_sessions
    ADD CONSTRAINT "PK_mfa_verified_sessions" PRIMARY KEY ("TokenHash");


--
-- Name: movie_genres PK_movie_genres; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.movie_genres
    ADD CONSTRAINT "PK_movie_genres" PRIMARY KEY ("MovieId", "GenreId");


--
-- Name: movies PK_movies; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.movies
    ADD CONSTRAINT "PK_movies" PRIMARY KEY ("Id");


--
-- Name: notifications PK_notifications; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.notifications
    ADD CONSTRAINT "PK_notifications" PRIMARY KEY ("Id");


--
-- Name: password_reset_tokens PK_password_reset_tokens; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.password_reset_tokens
    ADD CONSTRAINT "PK_password_reset_tokens" PRIMARY KEY ("Token");


--
-- Name: payments PK_payments; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.payments
    ADD CONSTRAINT "PK_payments" PRIMARY KEY ("Id");


--
-- Name: playback_issues PK_playback_issues; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.playback_issues
    ADD CONSTRAINT "PK_playback_issues" PRIMARY KEY ("Id");


--
-- Name: playback_telemetry PK_playback_telemetry; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.playback_telemetry
    ADD CONSTRAINT "PK_playback_telemetry" PRIMARY KEY ("Id");


--
-- Name: reviews PK_reviews; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.reviews
    ADD CONSTRAINT "PK_reviews" PRIMARY KEY ("Id");


--
-- Name: security_audit_events PK_security_audit_events; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.security_audit_events
    ADD CONSTRAINT "PK_security_audit_events" PRIMARY KEY ("Id");


--
-- Name: stream_sources PK_stream_sources; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.stream_sources
    ADD CONSTRAINT "PK_stream_sources" PRIMARY KEY ("EpisodeId", "Position");


--
-- Name: user_library PK_user_library; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_library
    ADD CONSTRAINT "PK_user_library" PRIMARY KEY ("UserId", "MovieId");


--
-- Name: user_security PK_user_security; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_security
    ADD CONSTRAINT "PK_user_security" PRIMARY KEY ("UserId");


--
-- Name: user_settings PK_user_settings; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_settings
    ADD CONSTRAINT "PK_user_settings" PRIMARY KEY ("UserId");


--
-- Name: users PK_users; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.users
    ADD CONSTRAINT "PK_users" PRIMARY KEY ("Id");


--
-- Name: vip_plans PK_vip_plans; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.vip_plans
    ADD CONSTRAINT "PK_vip_plans" PRIMARY KEY ("Id");


--
-- Name: watch_progress PK_watch_progress; Type: CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.watch_progress
    ADD CONSTRAINT "PK_watch_progress" PRIMARY KEY ("UserId", "MovieId");


--
-- Name: IX_access_tokens_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_access_tokens_UserId" ON webxemphim.access_tokens USING btree ("UserId");


--
-- Name: IX_active_streams_EpisodeId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_active_streams_EpisodeId" ON webxemphim.active_streams USING btree ("EpisodeId");


--
-- Name: IX_active_streams_ExpiresAtUtc; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_active_streams_ExpiresAtUtc" ON webxemphim.active_streams USING btree ("ExpiresAtUtc");


--
-- Name: IX_active_streams_MovieId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_active_streams_MovieId" ON webxemphim.active_streams USING btree ("MovieId");


--
-- Name: IX_active_streams_SessionId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_active_streams_SessionId" ON webxemphim.active_streams USING btree ("SessionId");


--
-- Name: IX_active_streams_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_active_streams_UserId" ON webxemphim.active_streams USING btree ("UserId");


--
-- Name: IX_active_streams_UserId_IsActive; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_active_streams_UserId_IsActive" ON webxemphim.active_streams USING btree ("UserId", "IsActive", "ExpiresAtUtc");


--
-- Name: IX_active_streams_UserId_IsActive_ExpiresAtUtc; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_active_streams_UserId_IsActive_ExpiresAtUtc" ON webxemphim.active_streams USING btree ("UserId", "IsActive", "ExpiresAtUtc");


--
-- Name: IX_audit_logs_AdminEmail; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_audit_logs_AdminEmail" ON webxemphim.audit_logs USING btree ("AdminEmail");


--
-- Name: IX_audit_logs_EntityType_EntityId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_audit_logs_EntityType_EntityId" ON webxemphim.audit_logs USING btree ("EntityType", "EntityId");


--
-- Name: IX_audit_logs_TimestampUtc; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_audit_logs_TimestampUtc" ON webxemphim.audit_logs USING btree ("TimestampUtc");


--
-- Name: IX_episodes_MovieId_EpisodeNumber; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_episodes_MovieId_EpisodeNumber" ON webxemphim.episodes USING btree ("MovieId", "EpisodeNumber");


--
-- Name: IX_genres_Slug; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_genres_Slug" ON webxemphim.genres USING btree ("Slug");


--
-- Name: IX_mfa_challenges_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_mfa_challenges_UserId" ON webxemphim.mfa_challenges USING btree ("UserId");


--
-- Name: IX_mfa_verified_sessions_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_mfa_verified_sessions_UserId" ON webxemphim.mfa_verified_sessions USING btree ("UserId");


--
-- Name: IX_movie_genres_GenreId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_movie_genres_GenreId" ON webxemphim.movie_genres USING btree ("GenreId");


--
-- Name: IX_movies_IsTrending_Rating; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_movies_IsTrending_Rating" ON webxemphim.movies USING btree ("IsTrending", "Rating");


--
-- Name: IX_movies_SearchText; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_movies_SearchText" ON webxemphim.movies USING btree ("SearchText");


--
-- Name: IX_movies_SearchVector; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_movies_SearchVector" ON webxemphim.movies USING gin (search_vector);


--
-- Name: IX_movies_Slug; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_movies_Slug" ON webxemphim.movies USING btree ("Slug");


--
-- Name: IX_movies_Type_ReleaseYear; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_movies_Type_ReleaseYear" ON webxemphim.movies USING btree ("Type", "ReleaseYear");


--
-- Name: IX_notifications_UserId_Read; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_notifications_UserId_Read" ON webxemphim.notifications USING btree ("UserId", "Read");


--
-- Name: IX_password_reset_tokens_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_password_reset_tokens_UserId" ON webxemphim.password_reset_tokens USING btree ("UserId");


--
-- Name: IX_payments_OrderCode; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_payments_OrderCode" ON webxemphim.payments USING btree ("OrderCode");


--
-- Name: IX_payments_TransactionReference; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_payments_TransactionReference" ON webxemphim.payments USING btree ("TransactionReference");


--
-- Name: IX_payments_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_payments_UserId" ON webxemphim.payments USING btree ("UserId");


--
-- Name: IX_playback_issues_EpisodeId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_playback_issues_EpisodeId" ON webxemphim.playback_issues USING btree ("EpisodeId");


--
-- Name: IX_playback_issues_MovieId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_playback_issues_MovieId" ON webxemphim.playback_issues USING btree ("MovieId");


--
-- Name: IX_playback_issues_UserId_MovieId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_playback_issues_UserId_MovieId" ON webxemphim.playback_issues USING btree ("UserId", "MovieId");


--
-- Name: IX_playback_telemetry_CreatedAtUtc; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_playback_telemetry_CreatedAtUtc" ON webxemphim.playback_telemetry USING btree ("CreatedAtUtc");


--
-- Name: IX_playback_telemetry_EpisodeId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_playback_telemetry_EpisodeId" ON webxemphim.playback_telemetry USING btree ("EpisodeId");


--
-- Name: IX_playback_telemetry_EventType_CreatedAtUtc; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_playback_telemetry_EventType_CreatedAtUtc" ON webxemphim.playback_telemetry USING btree ("EventType", "CreatedAtUtc");


--
-- Name: IX_reviews_MovieId_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_reviews_MovieId_UserId" ON webxemphim.reviews USING btree ("MovieId", "UserId");


--
-- Name: IX_reviews_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_reviews_UserId" ON webxemphim.reviews USING btree ("UserId");


--
-- Name: IX_security_audit_events_CreatedAtUtc; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_security_audit_events_CreatedAtUtc" ON webxemphim.security_audit_events USING btree ("CreatedAtUtc");


--
-- Name: IX_security_audit_events_UserId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_security_audit_events_UserId" ON webxemphim.security_audit_events USING btree ("UserId");


--
-- Name: IX_user_library_MovieId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_user_library_MovieId" ON webxemphim.user_library USING btree ("MovieId");


--
-- Name: IX_users_EmailLookupHash; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_users_EmailLookupHash" ON webxemphim.users USING btree ("EmailLookupHash");


--
-- Name: IX_users_NormalizedEmail; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE UNIQUE INDEX "IX_users_NormalizedEmail" ON webxemphim.users USING btree ("NormalizedEmail");


--
-- Name: IX_watch_progress_MovieId; Type: INDEX; Schema: webxemphim; Owner: -
--

CREATE INDEX "IX_watch_progress_MovieId" ON webxemphim.watch_progress USING btree ("MovieId");


--
-- Name: cast_members trg_cast_movies_search_vector; Type: TRIGGER; Schema: webxemphim; Owner: -
--

CREATE TRIGGER trg_cast_movies_search_vector AFTER INSERT OR DELETE OR UPDATE ON webxemphim.cast_members FOR EACH ROW EXECUTE FUNCTION webxemphim.webxemphim_refresh_movie_search_vector_for_cast();


--
-- Name: movies trg_movies_search_vector; Type: TRIGGER; Schema: webxemphim; Owner: -
--

CREATE TRIGGER trg_movies_search_vector AFTER INSERT OR UPDATE OF "Title", "OriginalTitle", "Director", "Overview" ON webxemphim.movies FOR EACH ROW EXECUTE FUNCTION webxemphim.webxemphim_refresh_movie_search_vector();


--
-- Name: access_tokens FK_access_tokens_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.access_tokens
    ADD CONSTRAINT "FK_access_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: active_streams FK_active_streams_episodes_EpisodeId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.active_streams
    ADD CONSTRAINT "FK_active_streams_episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES webxemphim.episodes("Id") ON DELETE SET NULL;


--
-- Name: active_streams FK_active_streams_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.active_streams
    ADD CONSTRAINT "FK_active_streams_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: active_streams FK_active_streams_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.active_streams
    ADD CONSTRAINT "FK_active_streams_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: cast_members FK_cast_members_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.cast_members
    ADD CONSTRAINT "FK_cast_members_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: episodes FK_episodes_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.episodes
    ADD CONSTRAINT "FK_episodes_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: mfa_challenges FK_mfa_challenges_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.mfa_challenges
    ADD CONSTRAINT "FK_mfa_challenges_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: mfa_credentials FK_mfa_credentials_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.mfa_credentials
    ADD CONSTRAINT "FK_mfa_credentials_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: mfa_verified_sessions FK_mfa_verified_sessions_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.mfa_verified_sessions
    ADD CONSTRAINT "FK_mfa_verified_sessions_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: movie_genres FK_movie_genres_genres_GenreId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.movie_genres
    ADD CONSTRAINT "FK_movie_genres_genres_GenreId" FOREIGN KEY ("GenreId") REFERENCES webxemphim.genres("Id") ON DELETE CASCADE;


--
-- Name: movie_genres FK_movie_genres_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.movie_genres
    ADD CONSTRAINT "FK_movie_genres_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: notifications FK_notifications_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.notifications
    ADD CONSTRAINT "FK_notifications_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: password_reset_tokens FK_password_reset_tokens_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.password_reset_tokens
    ADD CONSTRAINT "FK_password_reset_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: payments FK_payments_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.payments
    ADD CONSTRAINT "FK_payments_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: playback_issues FK_playback_issues_episodes_EpisodeId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.playback_issues
    ADD CONSTRAINT "FK_playback_issues_episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES webxemphim.episodes("Id") ON DELETE CASCADE;


--
-- Name: playback_issues FK_playback_issues_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.playback_issues
    ADD CONSTRAINT "FK_playback_issues_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: playback_issues FK_playback_issues_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.playback_issues
    ADD CONSTRAINT "FK_playback_issues_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: reviews FK_reviews_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.reviews
    ADD CONSTRAINT "FK_reviews_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: reviews FK_reviews_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.reviews
    ADD CONSTRAINT "FK_reviews_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: security_audit_events FK_security_audit_events_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.security_audit_events
    ADD CONSTRAINT "FK_security_audit_events_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE SET NULL;


--
-- Name: stream_sources FK_stream_sources_episodes_EpisodeId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.stream_sources
    ADD CONSTRAINT "FK_stream_sources_episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES webxemphim.episodes("Id") ON DELETE CASCADE;


--
-- Name: user_library FK_user_library_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_library
    ADD CONSTRAINT "FK_user_library_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: user_library FK_user_library_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_library
    ADD CONSTRAINT "FK_user_library_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: user_security FK_user_security_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_security
    ADD CONSTRAINT "FK_user_security_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: user_settings FK_user_settings_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.user_settings
    ADD CONSTRAINT "FK_user_settings_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- Name: watch_progress FK_watch_progress_movies_MovieId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.watch_progress
    ADD CONSTRAINT "FK_watch_progress_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES webxemphim.movies("Id") ON DELETE CASCADE;


--
-- Name: watch_progress FK_watch_progress_users_UserId; Type: FK CONSTRAINT; Schema: webxemphim; Owner: -
--

ALTER TABLE ONLY webxemphim.watch_progress
    ADD CONSTRAINT "FK_watch_progress_users_UserId" FOREIGN KEY ("UserId") REFERENCES webxemphim.users("Id") ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict RhQgpFYTQ49z3zfwT6jQoWabkknr1yAy3dDTnVuLiZawCSzTUHAujxdgmL8kfEX

