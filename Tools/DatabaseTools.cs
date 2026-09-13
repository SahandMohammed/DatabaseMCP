using System.ComponentModel;
using System.Text.Json;
using DreamItERP.DatabaseMcp.Services;
using ModelContextProtocol.Server;

namespace DreamItERP.DatabaseMcp.Tools;

[McpServerToolType]
public sealed class DatabaseTools
{
    [McpServerTool]
    [Description(
        "Returns information about the connected SQL Server " +
        "and current DreamItERP database.")]
    public static async Task<string> GetDatabaseInfo(
        DatabaseInspector database,
        CancellationToken cancellationToken)
    {
        var result =
            await database.GetDatabaseInfoAsync(
                cancellationToken);

        return JsonSerializer.Serialize(
            result,
            JsonOptions);
    }

    [McpServerTool]
    [Description(
        "Lists SQL Server tables and approximate row counts. " +
        "Use this when investigating the DreamItERP database schema.")]
    public static async Task<string> ListTables(
        DatabaseInspector database,
        CancellationToken cancellationToken)
    {
        var result =
            await database.ListTablesAsync(
                cancellationToken);

        return JsonSerializer.Serialize(
            result,
            JsonOptions);
    }

    [McpServerTool]
    [Description(
        "Returns the columns and SQL data types of a table.")]
    public static async Task<string> DescribeTable(
        DatabaseInspector database,

        [Description("Schema name, normally dbo")]
        string schema,

        [Description("SQL Server table name")]
        string table,

        CancellationToken cancellationToken)
    {
        var result =
            await database.DescribeTableAsync(
                schema,
                table,
                cancellationToken);

        return JsonSerializer.Serialize(
            result,
            JsonOptions);
    }

    [McpServerTool]
    [Description(
        "Lists indexes for a SQL Server table. " +
        "Useful when investigating slow DreamItERP queries.")]
    public static async Task<string> ListIndexes(
        DatabaseInspector database,

        [Description("Schema name, normally dbo")]
        string schema,

        [Description("SQL Server table name")]
        string table,

        CancellationToken cancellationToken)
    {
        var result =
            await database.ListIndexesAsync(
                schema,
                table,
                cancellationToken);

        return JsonSerializer.Serialize(
            result,
            JsonOptions);
    }

    [McpServerTool]
    [Description(
        "Reproduces the Inventory Dashboard inventory-value calculation " +
        "for one DreamItERP user using the current StockTable snapshot. " +
        "This tool is read-only and does not execute GetStockTable. " +
        "Open the Inventory Dashboard immediately before calling it so the " +
        "application refreshes StockTable first.")]
    public static async Task<string> GetInventoryDashboardValue(
        InventoryDashboardVerifier verifier,

        [Description(
            "DreamItERP UserTable.id for the user whose dashboard is being verified")]
        int userId,

        CancellationToken cancellationToken = default)
    {
        var result =
            await verifier.GetValueAsync(
                userId,
                cancellationToken);

        return JsonSerializer.Serialize(
            result,
            JsonOptions);
    }

    [McpServerTool]
    [Description(
        "Executes one read-only SELECT or WITH query against " +
        "the DreamItERP SQL Server database. " +
        "Never use this tool to modify database data or schema.")]
    public static async Task<string> QueryDatabase(
        DatabaseInspector database,

        [Description(
            "A single read-only SQL Server SELECT or WITH query")]
        string sql,

        [Description(
            "Maximum number of rows to return. Range 1-500.")]
        int maxRows = 100,

        CancellationToken cancellationToken = default)
    {
        var result =
            await database.QueryAsync(
                sql,
                maxRows,
                cancellationToken);

        return JsonSerializer.Serialize(
            result,
            JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };
}