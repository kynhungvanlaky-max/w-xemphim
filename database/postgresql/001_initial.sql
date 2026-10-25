-- ==============================================================================
-- CinemaFlow / WebXemPhim - PostgreSQL schema
-- ==============================================================================
-- á»¨ng dá»¥ng dÃ¹ng PostgreSQL lÃ m database chÃ­nh. Script nÃ y táº¡o toÃ n bá»™ schema:
-- báº£ng, chá»‰ má»¥c, rÃ ng buá»™c khÃ³a ngoáº¡i, hÃ m chuáº©n hoÃ¡ tiáº¿ng Viá»‡t vÃ  chá»‰ má»¥c
-- tÃ¬m kiáº¿m toÃ n vÄƒn (tsvector + GIN).
--
-- Script idempotent: cháº¡y láº¡i khÃ´ng xÃ³a dá»¯ liá»‡u tÃ i khoáº£n hiá»‡n cÃ³.
-- Server cÅ©ng tá»± Ã¡p dá»¥ng migration EF Core khi khá»Ÿi Ä‘á»™ng, nÃªn script nÃ y lÃ 
-- cÃ¡ch khá»Ÿi táº¡o thá»§ cÃ´ng cho mÃ´i trÆ°á»ng khÃ´ng dÃ¹ng Docker. Khi báº£ng Ä‘Ã£ tá»“n
-- táº¡i, server Ä‘Ã¡nh dáº¥u migration lÃ  "Ä‘Ã£ Ã¡p dá»¥ng" Ä‘á»ƒ khÃ´ng táº¡o láº¡i báº£ng.
--
-- YÃªu cáº§u quyá»n táº¡o extension "unaccent" (trusted extension tá»« PostgreSQL 13).
-- Náº¿u khÃ´ng cÃ³ quyá»n nÃ y, hÃ m webxemphim_normalize váº«n hoáº¡t Ä‘á»™ng nhÆ°ng chá»‰
-- chuáº©n hoÃ¡ Ä‘Æ°á»£c chá»¯ "Ä" vÃ  chá»¯ thÆ°á»ng.
--
--   psql -U webxemphim -d webxemphim -f database/postgresql/001_initial.sql
-- ==============================================================================
--
-- PostgreSQL database dump
--

\restrict F1IdtmrUzjme6EBlFzQoRi1BPrMqv7GaUeK1aIveJI3f1l4TZNGaZxqbqLKpu0B

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
-- Name: unaccent; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;


--
-- Name: EXTENSION unaccent; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION unaccent IS 'text search dictionary that removes accents';


--
-- Name: webxemphim_movie_search_vector(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE OR REPLACE FUNCTION public.webxemphim_movie_search_vector(p_movie_id integer) RETURNS tsvector
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
-- Name: webxemphim_normalize(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE OR REPLACE FUNCTION public.webxemphim_normalize(p_text text) RETURNS text
    LANGUAGE plpgsql STABLE PARALLEL SAFE
    AS $$
DECLARE
  v_text text;
BEGIN
  v_text := coalesce(p_text, '');
  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'unaccent') THEN
    v_text := public.unaccent(v_text);
  END IF;
  RETURN lower(regexp_replace(
           replace(replace(v_text, U&'\0110', 'D'), U&'\0111', 'd'),
           '\s+', ' ', 'g'));
END;
$$;


--
-- Name: webxemphim_refresh_movie_search_vector(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE OR REPLACE FUNCTION public.webxemphim_refresh_movie_search_vector() RETURNS trigger
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
-- Name: webxemphim_refresh_movie_search_vector_for_cast(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE OR REPLACE FUNCTION public.webxemphim_refresh_movie_search_vector_for_cast() RETURNS trigger
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
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: access_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.access_tokens (
    "Token" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: active_streams; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.active_streams (
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
-- Name: app_maintenance_flags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.app_maintenance_flags (
    "Flag" character varying(120) NOT NULL,
    "CompletedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.audit_logs (
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
-- Name: cast_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.cast_members (
    "MovieId" integer NOT NULL,
    "Position" integer NOT NULL,
    "Name" character varying(120) NOT NULL
);


--
-- Name: coupons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.coupons (
    "Code" character varying(32) NOT NULL,
    "DiscountType" character varying(32) NOT NULL,
    "DiscountValue" numeric(12,2) NOT NULL,
    "ValidUntilUtc" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL
);


--
-- Name: episodes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.episodes (
    "Id" integer NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeNumber" integer NOT NULL,
    "Title" character varying(240) NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "ThumbnailUrl" character varying(600) NOT NULL,
    "Overview" character varying(2000) NOT NULL
);


--
-- Name: episodes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = 'public'
           AND table_name = 'episodes'
           AND column_name = 'Id'
           AND is_identity = 'YES')
    THEN
        ALTER TABLE public.episodes ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (SEQUENCE NAME public."episodes_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1);
    END IF;
END $$;


--
-- Name: genres; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.genres (
    "Id" integer NOT NULL,
    "Name" character varying(80) NOT NULL,
    "Slug" character varying(80) NOT NULL
);


--
-- Name: genres_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = 'public'
           AND table_name = 'genres'
           AND column_name = 'Id'
           AND is_identity = 'YES')
    THEN
        ALTER TABLE public.genres ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (SEQUENCE NAME public."genres_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1);
    END IF;
END $$;


--
-- Name: mfa_challenges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.mfa_challenges (
    "TokenHash" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "FailedAttempts" integer NOT NULL
);


--
-- Name: mfa_credentials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.mfa_credentials (
    "UserId" uuid NOT NULL,
    "ProtectedSecret" character varying(1000) NOT NULL,
    "RecoveryCodeHashes" character varying(2000) NOT NULL,
    "Enabled" boolean NOT NULL,
    "EnabledAtUtc" timestamp with time zone
);


--
-- Name: mfa_verified_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.mfa_verified_sessions (
    "TokenHash" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "VerifiedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: movie_genres; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.movie_genres (
    "MovieId" integer NOT NULL,
    "GenreId" integer NOT NULL
);


--
-- Name: movies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.movies (
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
-- Name: movies_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = 'public'
           AND table_name = 'movies'
           AND column_name = 'Id'
           AND is_identity = 'YES')
    THEN
        ALTER TABLE public.movies ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (SEQUENCE NAME public."movies_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1);
    END IF;
END $$;


--
-- Name: notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.notifications (
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
-- Name: password_reset_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.password_reset_tokens (
    "Token" character varying(64) NOT NULL,
    "UserId" uuid NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "UsedAtUtc" timestamp with time zone
);


--
-- Name: payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.payments (
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
-- Name: playback_issues; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.playback_issues (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeId" integer NOT NULL,
    "Description" character varying(800) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.reviews (
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
-- Name: security_audit_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.security_audit_events (
    "Id" uuid NOT NULL,
    "UserId" uuid,
    "EventType" character varying(80) NOT NULL,
    "Success" boolean NOT NULL,
    "Detail" character varying(240) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: stream_sources; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.stream_sources (
    "EpisodeId" integer NOT NULL,
    "Position" integer NOT NULL,
    "ServerName" character varying(120) NOT NULL,
    "ServerType" integer NOT NULL,
    "Quality" character varying(32) NOT NULL,
    "StreamUrl" character varying(600) NOT NULL
);


--
-- Name: user_library; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.user_library (
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "AddedAt" timestamp with time zone NOT NULL
);


--
-- Name: user_security; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.user_security (
    "UserId" uuid NOT NULL,
    "IsAdmin" boolean NOT NULL,
    "PasswordIterations" integer NOT NULL,
    "FailedLoginAttempts" integer NOT NULL,
    "LockedUntilUtc" timestamp with time zone,
    "Version" bigint NOT NULL
);


--
-- Name: user_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.user_settings (
    "UserId" uuid NOT NULL,
    "DefaultQuality" character varying(32) NOT NULL,
    "AutoplayNext" boolean NOT NULL,
    "NotifyNewEpisodes" boolean NOT NULL,
    "Language" character varying(10) DEFAULT 'vi'::character varying NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.users (
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
-- Name: vip_plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.vip_plans (
    "Id" character varying(32) NOT NULL,
    "Name" character varying(80) NOT NULL,
    "Description" character varying(500) NOT NULL,
    "PriceVnd" numeric(12,2) NOT NULL,
    "DurationDays" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL
);


--
-- Name: watch_progress; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE IF NOT EXISTS public.watch_progress (
    "UserId" uuid NOT NULL,
    "MovieId" integer NOT NULL,
    "EpisodeId" integer,
    "EpisodeNumber" integer NOT NULL,
    "ProgressSeconds" integer NOT NULL,
    "DurationSeconds" integer NOT NULL,
    "LastWatchedAtUtc" timestamp with time zone NOT NULL
);


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK___EFMigrationsHistory')
    THEN
        ALTER TABLE ONLY public."__EFMigrationsHistory" ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");
    END IF;
END $$;


--
-- Name: access_tokens PK_access_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_access_tokens')
    THEN
        ALTER TABLE ONLY public.access_tokens ADD CONSTRAINT "PK_access_tokens" PRIMARY KEY ("Token");
    END IF;
END $$;


--
-- Name: active_streams PK_active_streams; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_active_streams')
    THEN
        ALTER TABLE ONLY public.active_streams ADD CONSTRAINT "PK_active_streams" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: app_maintenance_flags PK_app_maintenance_flags; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_app_maintenance_flags')
    THEN
        ALTER TABLE ONLY public.app_maintenance_flags ADD CONSTRAINT "PK_app_maintenance_flags" PRIMARY KEY ("Flag");
    END IF;
END $$;


--
-- Name: audit_logs PK_audit_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_audit_logs')
    THEN
        ALTER TABLE ONLY public.audit_logs ADD CONSTRAINT "PK_audit_logs" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: cast_members PK_cast_members; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_cast_members')
    THEN
        ALTER TABLE ONLY public.cast_members ADD CONSTRAINT "PK_cast_members" PRIMARY KEY ("MovieId", "Position");
    END IF;
END $$;


--
-- Name: coupons PK_coupons; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_coupons')
    THEN
        ALTER TABLE ONLY public.coupons ADD CONSTRAINT "PK_coupons" PRIMARY KEY ("Code");
    END IF;
END $$;


--
-- Name: episodes PK_episodes; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_episodes')
    THEN
        ALTER TABLE ONLY public.episodes ADD CONSTRAINT "PK_episodes" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: genres PK_genres; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_genres')
    THEN
        ALTER TABLE ONLY public.genres ADD CONSTRAINT "PK_genres" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: mfa_challenges PK_mfa_challenges; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_mfa_challenges')
    THEN
        ALTER TABLE ONLY public.mfa_challenges ADD CONSTRAINT "PK_mfa_challenges" PRIMARY KEY ("TokenHash");
    END IF;
END $$;


--
-- Name: mfa_credentials PK_mfa_credentials; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_mfa_credentials')
    THEN
        ALTER TABLE ONLY public.mfa_credentials ADD CONSTRAINT "PK_mfa_credentials" PRIMARY KEY ("UserId");
    END IF;
END $$;


--
-- Name: mfa_verified_sessions PK_mfa_verified_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_mfa_verified_sessions')
    THEN
        ALTER TABLE ONLY public.mfa_verified_sessions ADD CONSTRAINT "PK_mfa_verified_sessions" PRIMARY KEY ("TokenHash");
    END IF;
END $$;


--
-- Name: movie_genres PK_movie_genres; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_movie_genres')
    THEN
        ALTER TABLE ONLY public.movie_genres ADD CONSTRAINT "PK_movie_genres" PRIMARY KEY ("MovieId", "GenreId");
    END IF;
END $$;


--
-- Name: movies PK_movies; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_movies')
    THEN
        ALTER TABLE ONLY public.movies ADD CONSTRAINT "PK_movies" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: notifications PK_notifications; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_notifications')
    THEN
        ALTER TABLE ONLY public.notifications ADD CONSTRAINT "PK_notifications" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: password_reset_tokens PK_password_reset_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_password_reset_tokens')
    THEN
        ALTER TABLE ONLY public.password_reset_tokens ADD CONSTRAINT "PK_password_reset_tokens" PRIMARY KEY ("Token");
    END IF;
END $$;


--
-- Name: payments PK_payments; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_payments')
    THEN
        ALTER TABLE ONLY public.payments ADD CONSTRAINT "PK_payments" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: playback_issues PK_playback_issues; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_playback_issues')
    THEN
        ALTER TABLE ONLY public.playback_issues ADD CONSTRAINT "PK_playback_issues" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: reviews PK_reviews; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_reviews')
    THEN
        ALTER TABLE ONLY public.reviews ADD CONSTRAINT "PK_reviews" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: security_audit_events PK_security_audit_events; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_security_audit_events')
    THEN
        ALTER TABLE ONLY public.security_audit_events ADD CONSTRAINT "PK_security_audit_events" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: stream_sources PK_stream_sources; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_stream_sources')
    THEN
        ALTER TABLE ONLY public.stream_sources ADD CONSTRAINT "PK_stream_sources" PRIMARY KEY ("EpisodeId", "Position");
    END IF;
END $$;


--
-- Name: user_library PK_user_library; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_user_library')
    THEN
        ALTER TABLE ONLY public.user_library ADD CONSTRAINT "PK_user_library" PRIMARY KEY ("UserId", "MovieId");
    END IF;
END $$;


--
-- Name: user_security PK_user_security; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_user_security')
    THEN
        ALTER TABLE ONLY public.user_security ADD CONSTRAINT "PK_user_security" PRIMARY KEY ("UserId");
    END IF;
END $$;


--
-- Name: user_settings PK_user_settings; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_user_settings')
    THEN
        ALTER TABLE ONLY public.user_settings ADD CONSTRAINT "PK_user_settings" PRIMARY KEY ("UserId");
    END IF;
END $$;


--
-- Name: users PK_users; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_users')
    THEN
        ALTER TABLE ONLY public.users ADD CONSTRAINT "PK_users" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: vip_plans PK_vip_plans; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_vip_plans')
    THEN
        ALTER TABLE ONLY public.vip_plans ADD CONSTRAINT "PK_vip_plans" PRIMARY KEY ("Id");
    END IF;
END $$;


--
-- Name: watch_progress PK_watch_progress; Type: CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'PK_watch_progress')
    THEN
        ALTER TABLE ONLY public.watch_progress ADD CONSTRAINT "PK_watch_progress" PRIMARY KEY ("UserId", "MovieId");
    END IF;
END $$;


--
-- Name: IX_access_tokens_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_access_tokens_UserId" ON public.access_tokens USING btree ("UserId");


--
-- Name: IX_active_streams_EpisodeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_active_streams_EpisodeId" ON public.active_streams USING btree ("EpisodeId");


--
-- Name: IX_active_streams_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_active_streams_ExpiresAtUtc" ON public.active_streams USING btree ("ExpiresAtUtc");


--
-- Name: IX_active_streams_MovieId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_active_streams_MovieId" ON public.active_streams USING btree ("MovieId");


--
-- Name: IX_active_streams_SessionId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_active_streams_SessionId" ON public.active_streams USING btree ("SessionId");


--
-- Name: IX_active_streams_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_active_streams_UserId" ON public.active_streams USING btree ("UserId");


--
-- Name: IX_active_streams_UserId_IsActive_ExpiresAtUtc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_active_streams_UserId_IsActive_ExpiresAtUtc" ON public.active_streams USING btree ("UserId", "IsActive", "ExpiresAtUtc");


--
-- Name: IX_audit_logs_AdminEmail; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_audit_logs_AdminEmail" ON public.audit_logs USING btree ("AdminEmail");


--
-- Name: IX_audit_logs_EntityType_EntityId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_audit_logs_EntityType_EntityId" ON public.audit_logs USING btree ("EntityType", "EntityId");


--
-- Name: IX_audit_logs_TimestampUtc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_audit_logs_TimestampUtc" ON public.audit_logs USING btree ("TimestampUtc");


--
-- Name: IX_episodes_MovieId_EpisodeNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_episodes_MovieId_EpisodeNumber" ON public.episodes USING btree ("MovieId", "EpisodeNumber");


--
-- Name: IX_genres_Slug; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_genres_Slug" ON public.genres USING btree ("Slug");


--
-- Name: IX_mfa_challenges_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_mfa_challenges_UserId" ON public.mfa_challenges USING btree ("UserId");


--
-- Name: IX_mfa_verified_sessions_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_mfa_verified_sessions_UserId" ON public.mfa_verified_sessions USING btree ("UserId");


--
-- Name: IX_movie_genres_GenreId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_movie_genres_GenreId" ON public.movie_genres USING btree ("GenreId");


--
-- Name: IX_movies_IsTrending_Rating; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_movies_IsTrending_Rating" ON public.movies USING btree ("IsTrending", "Rating");


--
-- Name: IX_movies_Slug; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_movies_Slug" ON public.movies USING btree ("Slug");


--
-- Name: IX_movies_Type_ReleaseYear; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_movies_Type_ReleaseYear" ON public.movies USING btree ("Type", "ReleaseYear");


--
-- Name: IX_notifications_UserId_Read; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_notifications_UserId_Read" ON public.notifications USING btree ("UserId", "Read");


--
-- Name: IX_password_reset_tokens_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_password_reset_tokens_UserId" ON public.password_reset_tokens USING btree ("UserId");


--
-- Name: IX_payments_OrderCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_payments_OrderCode" ON public.payments USING btree ("OrderCode");


--
-- Name: IX_payments_TransactionReference; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_payments_TransactionReference" ON public.payments USING btree ("TransactionReference");


--
-- Name: IX_payments_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_payments_UserId" ON public.payments USING btree ("UserId");


--
-- Name: IX_playback_issues_EpisodeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_playback_issues_EpisodeId" ON public.playback_issues USING btree ("EpisodeId");


--
-- Name: IX_playback_issues_MovieId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_playback_issues_MovieId" ON public.playback_issues USING btree ("MovieId");


--
-- Name: IX_playback_issues_UserId_MovieId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_playback_issues_UserId_MovieId" ON public.playback_issues USING btree ("UserId", "MovieId");


--
-- Name: IX_reviews_MovieId_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_reviews_MovieId_UserId" ON public.reviews USING btree ("MovieId", "UserId");


--
-- Name: IX_reviews_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_reviews_UserId" ON public.reviews USING btree ("UserId");


--
-- Name: IX_security_audit_events_CreatedAtUtc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_security_audit_events_CreatedAtUtc" ON public.security_audit_events USING btree ("CreatedAtUtc");


--
-- Name: IX_security_audit_events_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_security_audit_events_UserId" ON public.security_audit_events USING btree ("UserId");


--
-- Name: IX_user_library_MovieId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_user_library_MovieId" ON public.user_library USING btree ("MovieId");


--
-- Name: IX_users_EmailLookupHash; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_users_EmailLookupHash" ON public.users USING btree ("EmailLookupHash");


--
-- Name: IX_users_NormalizedEmail; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_NormalizedEmail" ON public.users USING btree ("NormalizedEmail");


--
-- Name: IX_watch_progress_MovieId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_watch_progress_MovieId" ON public.watch_progress USING btree ("MovieId");


--
-- Name: ix_active_streams_expiresatutc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_active_streams_expiresatutc ON public.active_streams USING btree ("ExpiresAtUtc");


--
-- Name: ix_active_streams_sessionid; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX IF NOT EXISTS ix_active_streams_sessionid ON public.active_streams USING btree ("SessionId");


--
-- Name: ix_active_streams_userid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_active_streams_userid ON public.active_streams USING btree ("UserId");


--
-- Name: ix_active_streams_userid_isactive; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_active_streams_userid_isactive ON public.active_streams USING btree ("UserId", "IsActive", "ExpiresAtUtc");


--
-- Name: ix_audit_logs_adminemail; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_audit_logs_adminemail ON public.audit_logs USING btree ("AdminEmail");


--
-- Name: ix_audit_logs_entitytype_entityid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_audit_logs_entitytype_entityid ON public.audit_logs USING btree ("EntityType", "EntityId");


--
-- Name: ix_audit_logs_timestamputc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_audit_logs_timestamputc ON public.audit_logs USING btree ("TimestampUtc");


--
-- Name: ix_mfa_challenges_userid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_mfa_challenges_userid ON public.mfa_challenges USING btree ("UserId");


--
-- Name: ix_mfa_verified_sessions_userid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_mfa_verified_sessions_userid ON public.mfa_verified_sessions USING btree ("UserId");


--
-- Name: ix_movies_searchtext; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_movies_searchtext ON public.movies USING btree ("SearchText");


--
-- Name: ix_movies_searchvector; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS "IX_movies_SearchVector" ON public.movies USING gin (search_vector);


--
-- Name: ix_notifications_userid_read; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_notifications_userid_read ON public.notifications USING btree ("UserId", "Read");


--
-- Name: ix_payments_transactionreference; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_payments_transactionreference ON public.payments USING btree ("TransactionReference");


--
-- Name: ix_playback_issues_userid_movieid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_playback_issues_userid_movieid ON public.playback_issues USING btree ("UserId", "MovieId");


--
-- Name: ix_security_audit_events_createdatutc; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_security_audit_events_createdatutc ON public.security_audit_events USING btree ("CreatedAtUtc");


--
-- Name: ix_security_audit_events_userid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_security_audit_events_userid ON public.security_audit_events USING btree ("UserId");


--
-- Name: ix_user_library_movieid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_user_library_movieid ON public.user_library USING btree ("MovieId");


--
-- Name: ix_users_emaillookuphash; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_users_emaillookuphash ON public.users USING btree ("EmailLookupHash");


--
-- Name: ix_watch_progress_movieid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX IF NOT EXISTS ix_watch_progress_movieid ON public.watch_progress USING btree ("MovieId");


--
-- Name: cast_members trg_cast_movies_search_vector; Type: TRIGGER; Schema: public; Owner: -
--

DROP TRIGGER IF EXISTS trg_cast_movies_search_vector ON public.cast_members;
CREATE TRIGGER trg_cast_movies_search_vector AFTER INSERT OR DELETE OR UPDATE ON public.cast_members FOR EACH ROW EXECUTE FUNCTION public.webxemphim_refresh_movie_search_vector_for_cast();


--
-- Name: movies trg_movies_search_vector; Type: TRIGGER; Schema: public; Owner: -
--

DROP TRIGGER IF EXISTS trg_movies_search_vector ON public.movies;
CREATE TRIGGER trg_movies_search_vector AFTER INSERT OR UPDATE OF "Title", "OriginalTitle", "Director", "Overview" ON public.movies FOR EACH ROW EXECUTE FUNCTION public.webxemphim_refresh_movie_search_vector();


--
-- Name: access_tokens FK_access_tokens_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_access_tokens_users_UserId')
    THEN
        ALTER TABLE ONLY public.access_tokens ADD CONSTRAINT "FK_access_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: active_streams FK_active_streams_episodes_EpisodeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_active_streams_episodes_EpisodeId')
    THEN
        ALTER TABLE ONLY public.active_streams ADD CONSTRAINT "FK_active_streams_episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES public.episodes("Id") ON DELETE SET NULL;
    END IF;
END $$;


--
-- Name: active_streams FK_active_streams_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_active_streams_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.active_streams ADD CONSTRAINT "FK_active_streams_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: active_streams FK_active_streams_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_active_streams_users_UserId')
    THEN
        ALTER TABLE ONLY public.active_streams ADD CONSTRAINT "FK_active_streams_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: cast_members FK_cast_members_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_cast_members_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.cast_members ADD CONSTRAINT "FK_cast_members_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: episodes FK_episodes_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_episodes_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.episodes ADD CONSTRAINT "FK_episodes_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: mfa_challenges FK_mfa_challenges_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_mfa_challenges_users_UserId')
    THEN
        ALTER TABLE ONLY public.mfa_challenges ADD CONSTRAINT "FK_mfa_challenges_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: mfa_credentials FK_mfa_credentials_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_mfa_credentials_users_UserId')
    THEN
        ALTER TABLE ONLY public.mfa_credentials ADD CONSTRAINT "FK_mfa_credentials_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: mfa_verified_sessions FK_mfa_verified_sessions_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_mfa_verified_sessions_users_UserId')
    THEN
        ALTER TABLE ONLY public.mfa_verified_sessions ADD CONSTRAINT "FK_mfa_verified_sessions_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: movie_genres FK_movie_genres_genres_GenreId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_movie_genres_genres_GenreId')
    THEN
        ALTER TABLE ONLY public.movie_genres ADD CONSTRAINT "FK_movie_genres_genres_GenreId" FOREIGN KEY ("GenreId") REFERENCES public.genres("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: movie_genres FK_movie_genres_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_movie_genres_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.movie_genres ADD CONSTRAINT "FK_movie_genres_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: notifications FK_notifications_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_notifications_users_UserId')
    THEN
        ALTER TABLE ONLY public.notifications ADD CONSTRAINT "FK_notifications_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: password_reset_tokens FK_password_reset_tokens_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_password_reset_tokens_users_UserId')
    THEN
        ALTER TABLE ONLY public.password_reset_tokens ADD CONSTRAINT "FK_password_reset_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: payments FK_payments_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_payments_users_UserId')
    THEN
        ALTER TABLE ONLY public.payments ADD CONSTRAINT "FK_payments_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: playback_issues FK_playback_issues_episodes_EpisodeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_playback_issues_episodes_EpisodeId')
    THEN
        ALTER TABLE ONLY public.playback_issues ADD CONSTRAINT "FK_playback_issues_episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES public.episodes("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: playback_issues FK_playback_issues_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_playback_issues_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.playback_issues ADD CONSTRAINT "FK_playback_issues_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: playback_issues FK_playback_issues_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_playback_issues_users_UserId')
    THEN
        ALTER TABLE ONLY public.playback_issues ADD CONSTRAINT "FK_playback_issues_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: reviews FK_reviews_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_reviews_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.reviews ADD CONSTRAINT "FK_reviews_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: reviews FK_reviews_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_reviews_users_UserId')
    THEN
        ALTER TABLE ONLY public.reviews ADD CONSTRAINT "FK_reviews_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: stream_sources FK_stream_sources_episodes_EpisodeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_stream_sources_episodes_EpisodeId')
    THEN
        ALTER TABLE ONLY public.stream_sources ADD CONSTRAINT "FK_stream_sources_episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES public.episodes("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: user_library FK_user_library_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_user_library_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.user_library ADD CONSTRAINT "FK_user_library_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: user_library FK_user_library_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_user_library_users_UserId')
    THEN
        ALTER TABLE ONLY public.user_library ADD CONSTRAINT "FK_user_library_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: user_security FK_user_security_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_user_security_users_UserId')
    THEN
        ALTER TABLE ONLY public.user_security ADD CONSTRAINT "FK_user_security_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: user_settings FK_user_settings_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_user_settings_users_UserId')
    THEN
        ALTER TABLE ONLY public.user_settings ADD CONSTRAINT "FK_user_settings_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: watch_progress FK_watch_progress_movies_MovieId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_watch_progress_movies_MovieId')
    THEN
        ALTER TABLE ONLY public.watch_progress ADD CONSTRAINT "FK_watch_progress_movies_MovieId" FOREIGN KEY ("MovieId") REFERENCES public.movies("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- Name: watch_progress FK_watch_progress_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_watch_progress_users_UserId')
    THEN
        ALTER TABLE ONLY public.watch_progress ADD CONSTRAINT "FK_watch_progress_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;
    END IF;
END $$;


--
-- PostgreSQL database dump complete
--

\unrestrict F1IdtmrUzjme6EBlFzQoRi1BPrMqv7GaUeK1aIveJI3f1l4TZNGaZxqbqLKpu0B

