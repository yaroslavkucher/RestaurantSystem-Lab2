using RestaurantSystem.Models;

namespace RestaurantSystem.Interfaces;

public interface IOrderRepository
{
    Task<IEnumerable<Order>> GetActiveOrdersAsync();
    Task<int> CreateOrderAsync(int restaurantId, int tableId, int waiterId, int? customerId = null);
    Task AddOrderItemAsync(int orderId, int menuItemId, int quantity);
}