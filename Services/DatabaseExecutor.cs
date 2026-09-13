using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DreamItERP.DatabaseMcp.Services;

public sealed class DatabaseExecutor
{
    private const string AllowedDatabase = "DreamItERP";

    private readonly string _connectionString;

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

    private static readonly Regex ForbiddenExec = new(
        @"\b(
            SP_EXECUTESQL|
            OPENQUERY|
            OPENROWSET|
            OPENDATASOURCE|
            USE
        )\b|\bXP_[A-Za-z0-9_]*\b|\bSP_OA[A-Za-z0-9_]*\b|\bAT\s+[A-Za-z_\[]",
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.Compiled);

    public DatabaseExecutor(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Database is not configured.");

        var builder = new SqlConnectionStringBuilder(_connectionString);
        if (!string.Equals(
                builder.InitialCatalog,
                AllowedDatabase,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Database MCP is restricted to '{AllowedDatabase}'.");
        }
    }

    public async Task<int> ExecuteAsync(
        string sql,
        CancellationToken ct)
    {
        string normalized = ValidateExec(sql);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        if (!string.Equals(
                connection.Database,
                AllowedDatabase,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Database MCP is restricted to '{AllowedDatabase}'.");
        }

        await using var command = new SqlCommand(normalized, connection)
        {
            CommandTimeout = 600
        };

        return await command.ExecuteNonQueryAsync(ct);
    }

    private static string ValidateExec(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL cannot be empty.");

        string normalized = sql.Trim();

        if (normalized.EndsWith(';'))
            normalized = normalized[..^1].Trim();

        if (normalized.Contains(';'))
            throw new InvalidOperationException(
                "Multiple SQL statements are not allowed.");

        if (!Regex.IsMatch(
                normalized,
                @"^EXEC(?:UTE)?\b",
                RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException(
                "Only EXEC or EXECUTE statements are allowed by this tool.");
        }

        if (CrossDatabaseObject.IsMatch(normalized) ||
            CrossDatabaseShorthand.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                $"Cross-database execution is not allowed. " +
                $"This MCP is restricted to '{AllowedDatabase}'.");
        }

        if (ForbiddenExec.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                "Dynamic SQL, linked-server execution, and external data access are not allowed.");
        }

        return normalized;
    }
}
