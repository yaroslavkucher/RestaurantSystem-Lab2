using RestaurantSystem.Models;

namespace RestaurantSystem.Interfaces;

public interface IMenuRepository
{
    Task<IEnumerable<MenuItem>> GetActiveMenuItemsAsync();
    
    Task UpdatePriceAsync(int menuItemId, decimal newPrice, int updatedByUserId);
}