namespace DreamItERP.DatabaseMcp.Models;

public sealed record DatabaseInfo(
    string Server,
    string Database,
    string Version);

public sealed record TableInfo(
    string Schema,
    string Name,
    long RowCount);

public sealed record ColumnInfo(
    string Name,
    string DataType,
    bool Nullable,
    int? MaxLength,
    int? Precision,
    int? Scale);

public sealed record IndexInfo(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    string Columns);

public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int ReturnedRows,
    bool Truncated);

public sealed record InventoryDashboardValueResult(
    int UserId,
    bool CanViewStockValue,
    decimal InventoryValue,
    int TrackedItems,
    int PositiveStockRows,
    int AccessibleWarehouses,
    long StockTableRows);