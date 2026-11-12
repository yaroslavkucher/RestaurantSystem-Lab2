using RestaurantSystem.Lab2.Models;

namespace RestaurantSystem.Lab2;

/// <summary>
/// Спільний контракт для SQL- та MongoDB-реалізацій сховища відгуків.
/// Обидві реалізації повертають ОДНАКОВІ повні об'єкти Review,
/// тому порівняння швидкості — "яблука з яблуками".
/// </summary>
public interface IReviewStore
{
    string Name { get; }

    /// <summary>Мінімальний round-trip до сервера (SELECT 1 / ping) — базова лінія мережі/драйвера.</summary>
    Task PingAsync();

    Task<Review?> GetByIdAsync(int id);
    Task<List<Review>> GetLatestForRestaurantAsync(int restaurantId, int take);
    Task<List<Review>> FindByTagAsync(int restaurantId, string tag, int take);

    Task InsertAsync(Review review);
    Task InsertManyAsync(IReadOnlyList<Review> reviews);
    Task SetReplyAsync(int reviewId, ReviewReply reply);

    Task<List<DishStat>> GetDishStatsAsync(int restaurantId);
    Task<List<AspectStat>> GetAspectStatsAsync(int restaurantId);
    Task<List<RestaurantStat>> GetRestaurantStatsAsync();

    Task<long> CountAsync();
    /// <summary>Видаляє відгуки з Id &gt; maxId (прибирання після тестів вставки).</summary>
    Task DeleteAboveAsync(int maxId);
}
