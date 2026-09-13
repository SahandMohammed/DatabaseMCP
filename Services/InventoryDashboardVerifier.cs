using System.Data;
using DreamItERP.DatabaseMcp.Models;
using Microsoft.Data.SqlClient;

namespace DreamItERP.DatabaseMcp.Services;

public sealed class InventoryDashboardVerifier
{
    private const string AllowedDatabase = "DreamItERP";
    private const int WarehouseAccountCode = 131;

    private readonly string _connectionString;

    public InventoryDashboardVerifier(IConfiguration configuration)
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

    public async Task<InventoryDashboardValueResult> GetValueAsync(
        int userId,
        CancellationToken ct)
    {
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

        // This intentionally does not execute GetStockTable. The MCP remains
        // read-only. Open the Inventory Dashboard immediately before calling
        // this tool so StockTable is refreshed by the application itself.
        const string sql = """
;WITH StockRows AS
(
    SELECT
        s.warehouseCodeNo,
        s.item_id,
        s.branch_id,
        CAST(
            ISNULL(SUM(CAST(
                ISNULL(s.quantityPurchase,0)
                + ISNULL(s.quantityFirstCycleStock,0)
                + ISNULL(s.quantitySalesReturn,0)
                + ISNULL(s.quantityTransferIn,0)
                + ISNULL(s.quantityStockAdjustmentIn,0)
                + ISNULL(s.quantityProductionPlan,0)
                AS decimal(38,10))),0)
            -
            ISNULL(SUM(CAST(
                ISNULL(s.quantityPurchaseReturn,0)
                + ISNULL(s.quantitySales,0)
                + ISNULL(s.quantityWastage,0)
                + ISNULL(s.quantityTransferOut,0)
                + ISNULL(s.quantityStockAdjustmentOut,0)
                + ISNULL(s.quantityPPBOM,0)
                + ISNULL(s.quantityUsage,0)
                AS decimal(38,10))),0)
            AS decimal(38,6)) AS quantity,
        ISNULL(i.purchase_price,0) AS purchase_price
    FROM StockTable s
    INNER JOIN AccountTable a
        ON a.accounts_codeNo=s.warehouseCodeNo
    INNER JOIN ItemTable i
        ON i.id=s.item_id
    INNER JOIN SubcategoryTable sc
        ON sc.id=i.subcategory_id
    INNER JOIN CategoryTable cat
        ON cat.id=sc.category_id
    LEFT JOIN UnitFactorTable uf
        ON uf.unit_id=i.unit_id
       AND uf.isPrimary=1
    WHERE EXISTS
    (
        SELECT 1
        FROM UserOfBranchTable ub
        INNER JOIN BranchTable b
            ON b.id=ub.branch_id
        WHERE ub.user_id=@userId
          AND ub.branch_id=s.branch_id
          AND ub.isActive=1
          AND b.isActive=1
    )
      AND a.isActive=1
      AND LEFT(CONVERT(nvarchar(30),a.accounts_codeNo),3)='131'
      AND a.accounts_codeNo<>@warehouseRoot
      AND a.id NOT IN
      (
          SELECT ua.account_id
          FROM UserOfAccountTable ua
          WHERE ua.user_id=@userId
            AND ua.codeNo=@warehouseRoot
      )
    GROUP BY
        s.warehouseCodeNo,
        s.item_id,
        s.branch_id,
        uf.shortName,
        uf.unitName,
        i.purchase_price,
        i.minStock,
        i.maxStock,
        cat.name,
        i.code,
        i.name,
        a.name,
        a.codeNo
),
Permission AS
(
    SELECT CAST(CASE WHEN EXISTS
    (
        SELECT TOP(1) 1
        FROM UserRoleTable r
        INNER JOIN UserRoleAccessTable ura
            ON ura.userRole_id=r.id
        INNER JOIN UserTable u
            ON u.userRoleList_id=ura.userRoleList_id
        WHERE u.id=@userId
          AND r.name='HidePurchasePrice'
    ) THEN 1 ELSE 0 END AS bit) AS CanViewStockValue
)
SELECT
    @userId AS UserId,
    p.CanViewStockValue,
    CAST(
        CASE WHEN p.CanViewStockValue=1
             THEN ISNULL(SUM(
                    CASE WHEN sr.quantity>0
                         THEN sr.quantity*sr.purchase_price
                         ELSE 0 END),0)
             ELSE 0 END
        AS decimal(38,6)) AS InventoryValue,
    COUNT(DISTINCT CASE WHEN sr.item_id>0 THEN sr.item_id END) AS TrackedItems,
    SUM(CASE WHEN sr.quantity>0 THEN 1 ELSE 0 END) AS PositiveStockRows,
    (
        SELECT COUNT(*)
        FROM AccountTable wa
        WHERE wa.isActive=1
          AND LEFT(CONVERT(nvarchar(30),wa.accounts_codeNo),3)='131'
          AND wa.accounts_codeNo<>@warehouseRoot
          AND wa.id NOT IN
          (
              SELECT ua.account_id
              FROM UserOfAccountTable ua
              WHERE ua.user_id=@userId
                AND ua.codeNo=@warehouseRoot
          )
    ) AS AccessibleWarehouses,
    (SELECT COUNT_BIG(*) FROM StockTable) AS StockTableRows
FROM StockRows sr
CROSS JOIN Permission p
GROUP BY p.CanViewStockValue;
""";

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 600
        };
        command.Parameters.Add("@userId", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@warehouseRoot", SqlDbType.Int).Value = WarehouseAccountCode;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new InventoryDashboardValueResult(
                userId,
                false,
                0,
                0,
                0,
                0,
                0);
        }

        return new InventoryDashboardValueResult(
            Convert.ToInt32(reader["UserId"]),
            Convert.ToBoolean(reader["CanViewStockValue"]),
            Convert.ToDecimal(reader["InventoryValue"]),
            Convert.ToInt32(reader["TrackedItems"]),
            Convert.ToInt32(reader["PositiveStockRows"]),
            Convert.ToInt32(reader["AccessibleWarehouses"]),
            Convert.ToInt64(reader["StockTableRows"]));
    }
}
