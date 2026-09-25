CREATE DATABASE IF NOT EXISTS webxemphim
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE webxemphim;

CREATE TABLE IF NOT EXISTS users (
    Id CHAR(36) NOT NULL,
    FullName VARCHAR(80) NOT NULL,
    Email VARCHAR(160) NOT NULL,
    NormalizedEmail VARCHAR(160) NOT NULL,
    PasswordSalt VARBINARY(32) NOT NULL,
    PasswordHash VARBINARY(64) NOT NULL,
    Membership INT NOT NULL DEFAULT 0,
    VipExpiresAtUtc DATETIME(6) NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    CONSTRAINT PK_users PRIMARY KEY (Id),
    CONSTRAINT UQ_users_NormalizedEmail UNIQUE (NormalizedEmail)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS access_tokens (
    Token VARCHAR(64) NOT NULL,
    UserId CHAR(36) NOT NULL,
    ExpiresAtUtc DATETIME(6) NOT NULL,
    CONSTRAINT PK_access_tokens PRIMARY KEY (Token),
    CONSTRAINT FK_access_tokens_users_UserId
        FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
    INDEX IX_access_tokens_UserId (UserId),
    INDEX IX_access_tokens_ExpiresAtUtc (ExpiresAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS password_reset_tokens (
    Token VARCHAR(64) NOT NULL,
    UserId CHAR(36) NOT NULL,
    ExpiresAtUtc DATETIME(6) NOT NULL,
    UsedAtUtc DATETIME(6) NULL,
    CONSTRAINT PK_password_reset_tokens PRIMARY KEY (Token),
    CONSTRAINT FK_password_reset_tokens_users_UserId
        FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
    INDEX IX_password_reset_tokens_UserId (UserId),
    INDEX IX_password_reset_tokens_ExpiresAtUtc (ExpiresAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS payments (
    Id CHAR(36) NOT NULL,
    OrderCode VARCHAR(32) NOT NULL,
    UserId CHAR(36) NOT NULL,
    PlanId VARCHAR(32) NOT NULL,
    PlanName VARCHAR(80) NOT NULL,
    Amount DECIMAL(12,2) NOT NULL,
    Method INT NOT NULL,
    Status INT NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    PaidAtUtc DATETIME(6) NULL,
    VipExpiresAtUtc DATETIME(6) NULL,
    CONSTRAINT PK_payments PRIMARY KEY (Id),
    CONSTRAINT UQ_payments_OrderCode UNIQUE (OrderCode),
    CONSTRAINT FK_payments_users_UserId
        FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
    INDEX IX_payments_UserId_CreatedAtUtc (UserId, CreatedAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS genres (
    Id INT NOT NULL,
    Name VARCHAR(80) NOT NULL,
    Slug VARCHAR(80) NOT NULL,
    CONSTRAINT PK_genres PRIMARY KEY (Id),
    CONSTRAINT UQ_genres_Slug UNIQUE (Slug)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS movies (
    Id INT NOT NULL,
    Title VARCHAR(200) NOT NULL,
    OriginalTitle VARCHAR(200) NOT NULL,
    Slug VARCHAR(180) NOT NULL,
    Overview VARCHAR(2000) NOT NULL,
    PosterUrl VARCHAR(600) NOT NULL,
    BackdropUrl VARCHAR(600) NOT NULL,
    TrailerUrl VARCHAR(600) NULL,
    Rating DOUBLE NOT NULL,
    VoteCount INT NOT NULL,
    ReleaseYear INT NOT NULL,
    DurationMinutes INT NOT NULL,
    AgeRating VARCHAR(16) NOT NULL,
    Quality VARCHAR(32) NOT NULL,
    Type INT NOT NULL,
    Country VARCHAR(80) NOT NULL,
    ViewCount BIGINT NOT NULL,
    IsTrending TINYINT(1) NOT NULL,
    IsFeatured TINYINT(1) NOT NULL,
    Director VARCHAR(120) NOT NULL,
    TotalEpisodes INT NOT NULL,
    CurrentEpisode INT NOT NULL,
    CONSTRAINT PK_movies PRIMARY KEY (Id),
    CONSTRAINT UQ_movies_Slug UNIQUE (Slug),
    INDEX IX_movies_Type_ReleaseYear (Type, ReleaseYear),
    INDEX IX_movies_IsTrending_Rating (IsTrending, Rating)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS movie_genres (
    MovieId INT NOT NULL,
    GenreId INT NOT NULL,
    CONSTRAINT PK_movie_genres PRIMARY KEY (MovieId, GenreId),
    CONSTRAINT FK_movie_genres_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies (Id) ON DELETE CASCADE,
    CONSTRAINT FK_movie_genres_genres_GenreId FOREIGN KEY (GenreId) REFERENCES genres (Id) ON DELETE CASCADE,
    INDEX IX_movie_genres_GenreId (GenreId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cast_members (
    MovieId INT NOT NULL,
    Position INT NOT NULL,
    Name VARCHAR(120) NOT NULL,
    CONSTRAINT PK_cast_members PRIMARY KEY (MovieId, Position),
    CONSTRAINT FK_cast_members_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS episodes (
    Id INT NOT NULL,
    MovieId INT NOT NULL,
    EpisodeNumber INT NOT NULL,
    Title VARCHAR(240) NOT NULL,
    DurationMinutes INT NOT NULL,
    ThumbnailUrl VARCHAR(600) NOT NULL,
    Overview VARCHAR(2000) NOT NULL,
    CONSTRAINT PK_episodes PRIMARY KEY (Id),
    CONSTRAINT UQ_episodes_MovieId_EpisodeNumber UNIQUE (MovieId, EpisodeNumber),
    CONSTRAINT FK_episodes_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS stream_sources (
    EpisodeId INT NOT NULL,
    Position INT NOT NULL,
    ServerName VARCHAR(120) NOT NULL,
    ServerType INT NOT NULL,
    Quality VARCHAR(32) NOT NULL,
    StreamUrl VARCHAR(600) NOT NULL,
    CONSTRAINT PK_stream_sources PRIMARY KEY (EpisodeId, Position),
    CONSTRAINT FK_stream_sources_episodes_EpisodeId FOREIGN KEY (EpisodeId) REFERENCES episodes (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS reviews (
    Id CHAR(36) NOT NULL,
    MovieId INT NOT NULL,
    UserId CHAR(36) NOT NULL,
    Rating INT NOT NULL,
    Comment VARCHAR(800) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL,
    CONSTRAINT PK_reviews PRIMARY KEY (Id),
    CONSTRAINT UQ_reviews_MovieId_UserId UNIQUE (MovieId, UserId),
    CONSTRAINT CK_reviews_Rating CHECK (Rating BETWEEN 1 AND 10),
    CONSTRAINT FK_reviews_users_UserId
        FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
    CONSTRAINT FK_reviews_movies_MovieId
        FOREIGN KEY (MovieId) REFERENCES movies (Id) ON DELETE CASCADE,
    INDEX IX_reviews_MovieId_UpdatedAtUtc (MovieId, UpdatedAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
