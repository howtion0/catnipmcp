using System.Security.Cryptography;
using Catnip.DemoApi.Models;
using Microsoft.Data.Sqlite;

namespace Catnip.DemoApi.Configuration;

public sealed class WeatherCredentialStore
{
    public const string ProviderId = "qweather";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _databasePath;
    private readonly string _masterKeyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private static readonly object SqliteInitializationGate = new();
    private static bool _sqliteInitialized;

    public WeatherCredentialStore(Catnip.DemoApi.Runtime.DemoApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureSqliteInitialized();
        _databasePath = Path.Combine(options.DataRoot, "data", "gateway.db");
        _masterKeyPath = Path.Combine(options.DataRoot, "secrets", "mac-demo.masterkey");
    }

    private static void EnsureSqliteInitialized()
    {
        lock (SqliteInitializationGate)
        {
            if (_sqliteInitialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _sqliteInitialized = true;
        }
    }

    public async Task<WeatherCredentialView> GetViewAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            StoredCredential? stored = await ReadStoredAsync(cancellationToken).ConfigureAwait(false);
            return stored is null
                ? new WeatherCredentialView(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "北京",
                    null)
                : new WeatherCredentialView(
                    true,
                    stored.ApiHost,
                    stored.ProjectName,
                    stored.ProjectId,
                    stored.CredentialName,
                    stored.CredentialId,
                    "••••••••" + stored.KeySuffix,
                    stored.DefaultCity,
                    stored.UpdatedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        WeatherCredentialSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            StoredCredential? existing = await ReadStoredAsync(cancellationToken).ConfigureAwait(false);
            string apiKey = request.ApiKey.Trim();
            if (apiKey.Length == 0)
            {
                if (existing is null)
                {
                    throw new ArgumentException("首次保存必须填写 API KEY。", nameof(request));
                }

                apiKey = Decrypt(existing);
            }

            byte[] masterKey = await ReadMasterKeyAsync(cancellationToken).ConfigureAwait(false);
            byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(apiKey);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSize];
            try
            {
                using var aes = new AesGcm(masterKey, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
                await UpsertAsync(
                    request.ApiHost.Trim(),
                    request.ProjectName.Trim(),
                    request.ProjectId.Trim(),
                    request.CredentialName.Trim(),
                    request.CredentialId.Trim(),
                    request.DefaultCity.Trim(),
                    apiKey[^Math.Min(4, apiKey.Length)..],
                    ciphertext,
                    nonce,
                    tag,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(masterKey);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WeatherCredential?> GetCredentialAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            StoredCredential? stored = await ReadStoredAsync(cancellationToken).ConfigureAwait(false);
            return stored is null
                ? null
                : new WeatherCredential(
                    stored.ApiHost,
                    stored.CredentialId,
                    Decrypt(stored),
                    stored.DefaultCity);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static void ValidateRequest(WeatherCredentialSaveRequest request)
    {
        string host = request.ApiHost.Trim();
        if (host.Length > 0
            && (host.Length is < 8 or > 253
                || host.Contains('/', StringComparison.Ordinal)
                || !Uri.TryCreate($"https://{host}", UriKind.Absolute, out Uri? uri)
                || uri.Port != 443
                || !uri.Host.EndsWith(".qweatherapi.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("API Host 必须是和风控制台提供的专属 qweatherapi.com 主机名。", nameof(request));
        }

        if (request.CredentialId.Trim().Length is < 1 or > 64)
        {
            throw new ArgumentException("凭据 ID 长度必须为 1-64。", nameof(request));
        }

        if (request.ProjectId.Trim().Length is < 1 or > 64)
        {
            throw new ArgumentException("项目 ID 长度必须为 1-64。", nameof(request));
        }

        if (request.ProjectName.Trim().Length is < 1 or > 100)
        {
            throw new ArgumentException("项目名称长度必须为 1-100。", nameof(request));
        }

        if (request.CredentialName.Trim().Length is < 1 or > 100)
        {
            throw new ArgumentException("凭据名称长度必须为 1-100。", nameof(request));
        }

        if (request.ApiKey.Trim().Length > 256)
        {
            throw new ArgumentException("API KEY 长度不能超过 256。", nameof(request));
        }

        if (request.DefaultCity.Trim().Length is < 1 or > 50)
        {
            throw new ArgumentException("默认测试城市长度必须为 1-50。", nameof(request));
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_masterKeyPath)!);
        await EnsureMasterKeyAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS ExternalApiCredentials (
                ProviderId TEXT PRIMARY KEY,
                ApiHost TEXT NOT NULL,
                ProjectName TEXT NOT NULL,
                ProjectId TEXT NOT NULL,
                CredentialName TEXT NOT NULL,
                CredentialId TEXT NOT NULL,
                DefaultCity TEXT NOT NULL,
                KeySuffix TEXT NOT NULL,
                EncryptedKey BLOB NOT NULL,
                Nonce BLOB NOT NULL,
                Tag BLOB NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            PRAGMA user_version=1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private async Task EnsureMasterKeyAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_masterKeyPath))
        {
            return;
        }

        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        string temporaryPath = _masterKeyPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, key, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            try
            {
                File.Move(temporaryPath, _masterKeyPath);
            }
            catch (IOException) when (File.Exists(_masterKeyPath))
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<byte[]> ReadMasterKeyAsync(CancellationToken cancellationToken)
    {
        byte[] key = await File.ReadAllBytesAsync(_masterKeyPath, cancellationToken).ConfigureAwait(false);
        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("本机密钥文件无效。请恢复数据目录或重新配置天气凭据。");
        }

        return key;
    }

    private async Task<StoredCredential?> ReadStoredAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT ApiHost, ProjectName, ProjectId, CredentialName, CredentialId, DefaultCity, KeySuffix,
                   EncryptedKey, Nonce, Tag, UpdatedAt
            FROM ExternalApiCredentials
            WHERE ProviderId = $providerId;
            """;
        command.Parameters.AddWithValue("$providerId", ProviderId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredCredential(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            (byte[])reader[7],
            (byte[])reader[8],
            (byte[])reader[9],
            DateTimeOffset.Parse(reader.GetString(10), System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task UpsertAsync(
        string apiHost,
        string projectName,
        string projectId,
        string credentialName,
        string credentialId,
        string defaultCity,
        string keySuffix,
        byte[] encryptedKey,
        byte[] nonce,
        byte[] tag,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExternalApiCredentials
                (ProviderId, ApiHost, ProjectName, ProjectId, CredentialName, CredentialId, DefaultCity, KeySuffix,
                 EncryptedKey, Nonce, Tag, UpdatedAt)
            VALUES
                ($providerId, $apiHost, $projectName, $projectId, $credentialName, $credentialId, $defaultCity, $keySuffix,
                 $encryptedKey, $nonce, $tag, $updatedAt)
            ON CONFLICT(ProviderId) DO UPDATE SET
                ApiHost = excluded.ApiHost,
                ProjectName = excluded.ProjectName,
                ProjectId = excluded.ProjectId,
                CredentialName = excluded.CredentialName,
                CredentialId = excluded.CredentialId,
                DefaultCity = excluded.DefaultCity,
                KeySuffix = excluded.KeySuffix,
                EncryptedKey = excluded.EncryptedKey,
                Nonce = excluded.Nonce,
                Tag = excluded.Tag,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$providerId", ProviderId);
        command.Parameters.AddWithValue("$apiHost", apiHost);
        command.Parameters.AddWithValue("$projectName", projectName);
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$credentialName", credentialName);
        command.Parameters.AddWithValue("$credentialId", credentialId);
        command.Parameters.AddWithValue("$defaultCity", defaultCity);
        command.Parameters.AddWithValue("$keySuffix", keySuffix);
        command.Parameters.AddWithValue("$encryptedKey", encryptedKey);
        command.Parameters.AddWithValue("$nonce", nonce);
        command.Parameters.AddWithValue("$tag", tag);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string Decrypt(StoredCredential stored)
    {
        byte[] masterKey = File.ReadAllBytes(_masterKeyPath);
        byte[] plaintext = new byte[stored.EncryptedKey.Length];
        try
        {
            using var aes = new AesGcm(masterKey, TagSize);
            aes.Decrypt(stored.Nonce, stored.EncryptedKey, stored.Tag, plaintext);
            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private SqliteConnection OpenConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString());

    private sealed record StoredCredential(
        string ApiHost,
        string ProjectName,
        string ProjectId,
        string CredentialName,
        string CredentialId,
        string DefaultCity,
        string KeySuffix,
        byte[] EncryptedKey,
        byte[] Nonce,
        byte[] Tag,
        DateTimeOffset UpdatedAt);
}

public sealed record WeatherCredential(
    string ApiHost,
    string CredentialId,
    string ApiKey,
    string DefaultCity);
