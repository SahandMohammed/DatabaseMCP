using System.Data;
using System.Text.RegularExpressions;
using DreamItERP.DatabaseMcp.Models;
using Microsoft.Data.SqlClient;

namespace DreamItERP.DatabaseMcp.Services;

public sealed class DatabaseInspector
{
    private const string AllowedDatabase = "DreamItERP";

    private readonly string _connectionString;

    private static readonly Regex ForbiddenSql = new(
        @"\b(
            INSERT|
            UPDATE|
            DELETE|
            MERGE|
            DROP|
            ALTER|
            CREATE|
            TRUNCATE|
            GRANT|
            REVOKE|
            DENY|
            EXEC|
            EXECUTE|
            DBCC|
            BACKUP|
            RESTORE|
            BULK|
            OPENQUERY|
            OPENROWSET|
            OPENDATASOURCE|
            WAITFOR|
            USE
        )\b",
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.Compiled);

    private static readonly Regex CrossDatabaseObject = new(
        """
        (?:
            \[[^\]]+\] |
            "[^"]+" |
            [A-Za-z_#@][A-Za-z0-9_$#@]*
        )
        \s*\.\s*
        (?:
            \[[^\]]+\] |
            "[^"]+" |
            [A-Za-z_#@][A-Za-z0-9_$#@]*
        )
        \s*\.\s*
        (?:
            \[[^\]]+\] |
            "[^"]+" |
            [A-Za-z_#@][A-Za-z0-9_$#@]*
        )
        """,
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.Compiled);

    private static readonly Regex CrossDatabaseShorthand = new(
        """
        (?:
            \[[^\]]+\] |
            "[^"]+" |
            [A-Za-z_#@][A-Za-z0-9_$#@]*
        )
        \s*\.\s*\.\s*
        (?:
            \[[^\]]+\] |
            "[^"]+" |
            [A-Za-z_#@][A-Za-z0-9_$#@]*
        )
        """,
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.Compiled);

    public DatabaseInspector(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Database is not configured.");

        var connectionString = new SqlConnectionStringBuilder(_connectionString);

        if (!string.Equals(
                connectionString.InitialCatalog,
                AllowedDatabase,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Database MCP is restricted to '{AllowedDatabase}'. " +
                $"ConnectionStrings:Database must explicitly target that database.");
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(
        CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(ct);

            if (!string.Equals(
                    connection.Database,
                    AllowedDatabase,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Database MCP is restricted to '{AllowedDatabase}', " +
                    $"but the SQL connection opened database '{connection.Database}'.");
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<DatabaseInfo> GetDatabaseInfoAsync(
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);

        const string sql = """
SELECT
    @@SERVERNAME AS ServerName,
    DB_NAME() AS DatabaseName,
    CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100))
        AS Version;
""";

        await using var command = new SqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        await reader.ReadAsync(ct);

        return new DatabaseInfo(
            Convert.ToString(reader["ServerName"]) ?? "",
            Convert.ToString(reader["DatabaseName"]) ?? "",
            Convert.ToString(reader["Version"]) ?? "");
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);

        const string sql = """
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    SUM(ISNULL(p.rows, 0)) AS [RowCount]
FROM sys.tables t
INNER JOIN sys.schemas s
    ON s.schema_id = t.schema_id
LEFT JOIN sys.partitions p
    ON p.object_id = t.object_id
   AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
""";

        await using var command = new SqlCommand(sql, connection);
        await using var reader =
            await command.ExecuteReaderAsync(ct);

        var result = new List<TableInfo>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new TableInfo(
                Convert.ToString(reader["SchemaName"]) ?? "",
                Convert.ToString(reader["TableName"]) ?? "",
                Convert.ToInt64(reader["RowCount"])));
        }

        return result;
    }

    public async Task<IReadOnlyList<ColumnInfo>> DescribeTableAsync(
        string schema,
        string table,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);

        const string sql = """
SELECT
    c.name AS ColumnName,
    ty.name AS DataType,
    c.is_nullable AS IsNullable,
    CASE
        WHEN c.max_length = -1 THEN NULL
        WHEN ty.name IN ('nvarchar','nchar')
            THEN c.max_length / 2
        ELSE c.max_length
    END AS MaxLength,
    c.precision AS [Precision],
    c.scale AS Scale
FROM sys.columns c
INNER JOIN sys.tables t
    ON t.object_id = c.object_id
INNER JOIN sys.schemas s
    ON s.schema_id = t.schema_id
INNER JOIN sys.types ty
    ON ty.user_type_id = c.user_type_id
WHERE s.name = @schema
  AND t.name = @table
ORDER BY c.column_id;
""";

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add(
            "@schema",
            SqlDbType.NVarChar,
            128).Value = schema;

        command.Parameters.Add(
            "@table",
            SqlDbType.NVarChar,
            128).Value = table;

        var result = new List<ColumnInfo>();

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            result.Add(new ColumnInfo(
                Convert.ToString(reader["ColumnName"]) ?? "",
                Convert.ToString(reader["DataType"]) ?? "",
                Convert.ToBoolean(reader["IsNullable"]),
                reader["MaxLength"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(reader["MaxLength"]),
                reader["Precision"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(reader["Precision"]),
                reader["Scale"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(reader["Scale"])));
        }

        return result;
    }

    public async Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(
        string schema,
        string table,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);

        const string sql = """
SELECT
    i.name AS IndexName,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    STRING_AGG(
        CAST(c.name AS nvarchar(max)),
        ', '
    ) WITHIN GROUP (
        ORDER BY ic.key_ordinal
    ) AS Columns
FROM sys.indexes i
INNER JOIN sys.tables t
    ON t.object_id = i.object_id
INNER JOIN sys.schemas s
    ON s.schema_id = t.schema_id
INNER JOIN sys.index_columns ic
    ON ic.object_id = i.object_id
   AND ic.index_id = i.index_id
INNER JOIN sys.columns c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE s.name = @schema
  AND t.name = @table
  AND i.name IS NOT NULL
  AND ic.is_included_column = 0
GROUP BY
    i.name,
    i.is_unique,
    i.is_primary_key
ORDER BY i.name;
""";

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add(
            "@schema",
            SqlDbType.NVarChar,
            128).Value = schema;

        command.Parameters.Add(
            "@table",
            SqlDbType.NVarChar,
            128).Value = table;

        var result = new List<IndexInfo>();

        await using var reader =
            await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            result.Add(new IndexInfo(
                Convert.ToString(reader["IndexName"]) ?? "",
                Convert.ToBoolean(reader["IsUnique"]),
                Convert.ToBoolean(reader["IsPrimaryKey"]),
                Convert.ToString(reader["Columns"]) ?? ""));
        }

        return result;
    }

    public async Task<QueryResult> QueryAsync(
        string sql,
        int maxRows,
        CancellationToken ct)
    {
        ValidateReadOnlySql(sql);

        maxRows = Math.Clamp(maxRows, 1, 500);

        await using var connection = await OpenConnectionAsync(ct);

        await using var command =
            new SqlCommand(sql, connection)
            {
                CommandTimeout = 30
            };

        await using var reader =
            await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess,
                ct);

        var columns = Enumerable
            .Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToList();

        var rows =
            new List<Dictionary<string, object?>>();

        bool truncated = false;

        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }

            var row =
                new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < reader.FieldCount; i++)
            {
                object value = reader.GetValue(i);

                row[reader.GetName(i)] =
                    value == DBNull.Value
                        ? null
                        : NormalizeValue(value);
            }

            rows.Add(row);
        }

        return new QueryResult(
            columns,
            rows,
            rows.Count,
            truncated);
    }

    private static void ValidateReadOnlySql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException(
                "SQL cannot be empty.");

        string normalized = sql.Trim();

        if (normalized.EndsWith(';'))
            normalized = normalized[..^1].Trim();

        if (normalized.Contains(';'))
            throw new InvalidOperationException(
                "Multiple SQL statements are not allowed.");

        bool validStart =
            normalized.StartsWith(
                "SELECT",
                StringComparison.OrdinalIgnoreCase)
            ||
            normalized.StartsWith(
                "WITH",
                StringComparison.OrdinalIgnoreCase);

        if (!validStart)
            throw new InvalidOperationException(
                "Only SELECT or WITH queries are allowed.");

        if (CrossDatabaseObject.IsMatch(normalized) ||
            CrossDatabaseShorthand.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                $"Cross-database queries are not allowed. " +
                $"This MCP is restricted to '{AllowedDatabase}'.");
        }

        if (ForbiddenSql.IsMatch(normalized))
            throw new InvalidOperationException(
                "The query contains a forbidden SQL operation.");

        if (Regex.IsMatch(
                normalized,
                @"\bSELECT\s+.*\bINTO\b",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline))
        {
            throw new InvalidOperationException(
                "SELECT INTO is not allowed.");
        }
    }

    private static object NormalizeValue(object value)
    {
        return value switch
        {
            byte[] bytes =>
                Convert.ToBase64String(bytes),

            DateTime date =>
                date.ToString("O"),

            DateTimeOffset date =>
                date.ToString("O"),

            Guid guid =>
                guid.ToString(),

            _ => value
        };
    }
}