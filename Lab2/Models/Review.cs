using MongoDB.Bson.Serialization.Attributes;

namespace RestaurantSystem.Lab2.Models;

/// <summary>
/// Відгук клієнта про візит до ресторану.
/// У MongoDB — ОДИН документ колекції `reviews`.
/// У SQL — рядок у Reviews + рядки у ReviewAspectRatings, ReviewTags(+Tags),
/// ReviewDishRatings(+MenuItems), ReviewPhotos, ReviewReplies.
/// </summary>
public class Review
{
    [BsonId] public int Id { get; set; }

    [BsonElement("restaurantId")] public int RestaurantId { get; set; }
    [BsonElement("customerId")] public int CustomerId { get; set; }

    [BsonElement("orderId"), BsonIgnoreIfNull]
    public int? OrderId { get; set; }

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("rating")] public int Rating { get; set; }

    [BsonElement("comment"), BsonIgnoreIfNull]
    public string? Comment { get; set; }

    /// <summary>Довільний набір аспектів: food, service, ambience, ... (схема змінна).</summary>
    [BsonElement("aspects")] public Dictionary<string, int> Aspects { get; set; } = new();

    [BsonElement("tags")] public List<string> Tags { get; set; } = new();

    [BsonElement("dishes")] public List<DishRating> Dishes { get; set; } = new();

    [BsonElement("photos")] public List<ReviewPhoto> Photos { get; set; } = new();

    [BsonElement("reply"), BsonIgnoreIfNull]
    public ReviewReply? Reply { get; set; }

    [BsonElement("visit"), BsonIgnoreIfNull]
    public VisitInfo? Visit { get; set; }
}

public class DishRating
{
    [BsonElement("menuItemId")] public int MenuItemId { get; set; }

    /// <summary>Денормалізована назва страви (у SQL отримується JOIN-ом з MenuItems).</summary>
    [BsonElement("name")] public string Name { get; set; } = "";

    [BsonElement("rating")] public int Rating { get; set; }

    [BsonElement("comment"), BsonIgnoreIfNull]
    public string? Comment { get; set; }
}

public class ReviewPhoto
{
    [BsonElement("url")] public string Url { get; set; } = "";

    [BsonElement("caption"), BsonIgnoreIfNull]
    public string? Caption { get; set; }
}

public class ReviewReply
{
    [BsonElement("userId")] public int UserId { get; set; }
    [BsonElement("text")] public string Text { get; set; } = "";

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }
}

public class VisitInfo
{
    [BsonElement("guests")] public int Guests { get; set; }

    [BsonElement("occasion"), BsonIgnoreIfNull]
    public string? Occasion { get; set; }
}

public record DishStat(int MenuItemId, string Name, int ReviewsCount, double AvgRating);

public record RestaurantStat(int RestaurantId, int ReviewsCount, double AvgRating);

public record AspectStat(string Aspect, int Count, double AvgScore);
