using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class FieldEncryptionTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-enc-tests-{Guid.NewGuid():N}")
            .Options;

    private static (SqliteConnection connection, DbContextOptions<AppDbContext> options) CreateSqliteOptions()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        using var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return (connection, options);
    }

    [Fact]
    public void FieldEncryptor_EncryptAndDecrypt_RoundTripsSuccessfully()
    {
        var encryptor = new FieldEncryptor();
        const string originalEmail = "customer@cinemaflow.vn";
        const string originalName = "Nguyễn Văn Bảo";

        var encryptedEmail = encryptor.Encrypt(originalEmail);
        var encryptedName = encryptor.Encrypt(originalName);

        Assert.NotNull(encryptedEmail);
        Assert.NotNull(encryptedName);
        Assert.NotEqual(originalEmail, encryptedEmail);
        Assert.NotEqual(originalName, encryptedName);

        var decryptedEmail = encryptor.Decrypt(encryptedEmail);
        var decryptedName = encryptor.Decrypt(encryptedName);

        Assert.Equal(originalEmail, decryptedEmail);
        Assert.Equal(originalName, decryptedName);
    }

    [Fact]
    public void FieldEncryptor_ComputeLookupHash_IsDeterministicAndNormalized()
    {
        var encryptor = new FieldEncryptor();
        var hash1 = encryptor.ComputeLookupHash("Test.User@CinemaFlow.VN ");
        var hash2 = encryptor.ComputeLookupHash(" test.user@cinemaflow.vn");
        var hashDifferent = encryptor.ComputeLookupHash("other.user@cinemaflow.vn");

        Assert.NotNull(hash1);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2); // Case & whitespace insensitive
        Assert.NotEqual(hash1, hashDifferent);
    }

    [Fact]
    public void FieldEncryptor_DecryptLegacyPlaintext_ReturnsGracefullyWithoutThrowing()
    {
        var encryptor = new FieldEncryptor();
        const string legacyPlaintext = "legacy.user@cinema.test";

        var result = encryptor.Decrypt(legacyPlaintext);

        Assert.Equal(legacyPlaintext, result);
    }

    [Fact]
    public async Task Database_StoredValuesAreEncryptedAtRest_InSqlite()
    {
        var (connection, options) = CreateSqliteOptions();
        await using (connection)
        {
            var userId = Guid.NewGuid();
            const string plainEmail = "secret_agent@cinema.vn";
            const string plainName = "Điệp Viên 007";

            // 1. Save user via EF Core
            await using (var context = new AppDbContext(options))
            {
                var user = new UserEntity
                {
                    Id = userId,
                    Email = plainEmail,
                    FullName = plainName,
                    NormalizedEmail = plainEmail.ToUpperInvariant(),
                    EmailLookupHash = FieldEncryptor.Current.ComputeLookupHash(plainEmail),
                    PasswordSalt = [1, 2, 3],
                    PasswordHash = [4, 5, 6],
                    Membership = MembershipTier.Vip,
                    CreatedAtUtc = DateTime.UtcNow
                };
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }

            // 2. Query RAW database table directly via ADO.NET DbCommand
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Email, FullName, EmailLookupHash FROM users LIMIT 1;";

                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());

                var rawEmail = reader.GetString(0);
                var rawFullName = reader.GetString(1);
                var rawLookupHash = reader.GetString(2);

                // RAW values at rest must NOT equal plaintext!
                Assert.NotEqual(plainEmail, rawEmail);
                Assert.NotEqual(plainName, rawFullName);
                Assert.True(rawEmail.Length > plainEmail.Length, "Ciphertext should be longer than plaintext");
                Assert.True(rawFullName.Length > plainName.Length, "Ciphertext should be longer than plaintext");

                // Lookup hash is stored deterministically
                var expectedHash = FieldEncryptor.Current.ComputeLookupHash(plainEmail);
                Assert.Equal(expectedHash, rawLookupHash);
            }

            // 3. Query via EF Core: must decrypt transparently back to plaintext
            await using (var context = new AppDbContext(options))
            {
                var loadedUser = await context.Users.FirstAsync(u => u.Id == userId);
                Assert.Equal(plainEmail, loadedUser.Email);
                Assert.Equal(plainName, loadedUser.FullName);
            }
        }
    }

    [Fact]
    public async Task AuthService_RegistrationAndLogin_WorksWithEncryptedFieldsAndLookupHash()
    {
        var (connection, options) = CreateSqliteOptions();
        await using (connection)
        {
            var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AppDbContext(options));

            var authService = new AuthService(factoryMock.Object, TimeProvider.System);

            const string email = "crypto_user@cinema.test";
            const string password = "Password123!";
            const string name = "Bảo Mật Gia";

            // 1. Register new user
            var regResult = await authService.RegisterAsync(new RegisterRequest
            {
                FullName = name,
                Email = email,
                Password = password,
                ConfirmPassword = password,
                AcceptTerms = true
            });

            Assert.Equal(AuthFailure.None, regResult.Failure);
            Assert.NotNull(regResult.Data);
            Assert.Equal(name, regResult.Data.User.FullName);
            Assert.Equal(email, regResult.Data.User.Email);

            // Verify in raw DB that values are encrypted
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Email, FullName, EmailLookupHash FROM users LIMIT 1;";

                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.NotEqual(email, reader.GetString(0));
                Assert.NotEqual(name, reader.GetString(1));
                Assert.Equal(FieldEncryptor.Current.ComputeLookupHash(email), reader.GetString(2));
            }

            // 2. Duplicate registration with same email (different case) must be rejected
            var dupResult = await authService.RegisterAsync(new RegisterRequest
            {
                FullName = "Another User",
                Email = "  CRYPTO_USER@cinema.test  ",
                Password = password,
                ConfirmPassword = password,
                AcceptTerms = true
            });
            Assert.Equal(AuthFailure.DuplicateEmail, dupResult.Failure);

            // 3. Login with email and password must succeed
            var loginResult = await authService.LoginAsync(new LoginRequest
            {
                Email = "Crypto_User@cinema.test",
                Password = password
            });

            Assert.Equal(AuthFailure.None, loginResult.Failure);
            Assert.NotNull(loginResult.Data);
            Assert.Equal(name, loginResult.Data.User.FullName);
            Assert.Equal(email, loginResult.Data.User.Email);
        }
    }

    [Fact]
    public async Task AuthService_UpdateProfile_ReEncryptsAndUpdatesTransparently()
    {
        var (connection, options) = CreateSqliteOptions();
        await using (connection)
        {
            var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AppDbContext(options));

            var authService = new AuthService(factoryMock.Object, TimeProvider.System);

            const string email = "update_test@cinema.test";
            const string password = "Password123!";

            var regResult = await authService.RegisterAsync(new RegisterRequest
            {
                FullName = "Initial Name",
                Email = email,
                Password = password,
                ConfirmPassword = password,
                AcceptTerms = true
            });

            var userId = regResult.Data!.User.Id;

            // Update FullName
            const string updatedName = "Tên Mới Đã Cập Nhật";
            var updateSuccess = await authService.UpdateSettingsAsync(userId, new UserSettingsDto
            {
                FullName = updatedName,
                Language = "vi"
            });

            Assert.True(updateSuccess);

            // Verify RAW DB has updated ciphertext
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT FullName FROM users LIMIT 1;";
                var rawUpdatedName = (string)(await command.ExecuteScalarAsync())!;
                Assert.NotEqual(updatedName, rawUpdatedName);
                Assert.NotEqual("Initial Name", rawUpdatedName);
            }

            // Verify settings retrieval reads updated decrypted name
            var settings = await authService.GetSettingsAsync(userId);
            Assert.NotNull(settings);
            Assert.Equal(updatedName, settings.FullName);
        }
    }
}
