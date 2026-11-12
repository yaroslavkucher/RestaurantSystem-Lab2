using System.Data;
using Dapper;
using RestaurantSystem.Interfaces;
using RestaurantSystem.Models;

namespace RestaurantSystem.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly IDbTransaction _transaction;
    private IDbConnection Connection => _transaction.Connection!;

    public OrderRepository(IDbTransaction transaction)
    {
        _transaction = transaction;
    }

    public async Task<IEnumerable<Order>> GetActiveOrdersAsync()
    {
        string sql = "SELECT * FROM vw_ActiveOrders";
        return await Connection.QueryAsync<Order>(sql, transaction: _transaction);
    }

    public async Task<int> CreateOrderAsync(int restaurantId, int tableId, int waiterId, int? customerId = null)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@RestaurantId", restaurantId);
        parameters.Add("@TableId", tableId);
        parameters.Add("@WaiterId", waiterId);
        parameters.Add("@CustomerId", customerId);
        parameters.Add("@NewOrderId", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await Connection.ExecuteAsync("sp_CreateOrder", parameters, 
            transaction: _transaction, commandType: CommandType.StoredProcedure);

        return parameters.Get<int>("@NewOrderId");
    }

    public async Task AddOrderItemAsync(int orderId, int menuItemId, int quantity)
    {
        var parameters = new { OrderId = orderId, MenuItemId = menuItemId, Quantity = quantity };
        
        await Connection.ExecuteAsync("sp_AddOrderItem", parameters, 
            transaction: _transaction, commandType: CommandType.StoredProcedure);
    }
}