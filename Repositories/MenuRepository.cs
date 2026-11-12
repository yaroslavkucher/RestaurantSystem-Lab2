using System.Data;
using Dapper;
using RestaurantSystem.Interfaces;
using RestaurantSystem.Models;

namespace RestaurantSystem.Repositories;

public class MenuRepository : IMenuRepository
{
    private readonly IDbTransaction _transaction;
    private IDbConnection Connection => _transaction.Connection!;

    public MenuRepository(IDbTransaction transaction)
    {
        _transaction = transaction;
    }

    public async Task<IEnumerable<MenuItem>> GetActiveMenuItemsAsync()
    {
        string sql = "SELECT * FROM vw_ActiveMenuItems";
        return await Connection.QueryAsync<MenuItem>(sql, transaction: _transaction);
    }

    public async Task UpdatePriceAsync(int menuItemId, decimal newPrice, int updatedByUserId)
    {
        var parameters = new { MenuItemId = menuItemId, NewPrice = newPrice, UpdatedByUserId = updatedByUserId };
        
        await Connection.ExecuteAsync("sp_UpdateMenuItemPrice", parameters, 
            transaction: _transaction, commandType: CommandType.StoredProcedure);
    }
}