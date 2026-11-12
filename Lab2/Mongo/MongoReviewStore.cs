using MongoDB.Bson;
using MongoDB.Driver;
using RestaurantSystem.Lab2.Models;

namespace RestaurantSystem.Lab2.Mongo;

/// <summary>
/// Документна реалізація: один відгук = один документ у колекції `reviews`.
/// Посилання на реляційні сутності (restaurantId, customerId, menuItemId, userId) зберігаються як int —
/// це поліглотне зберігання: "довідники" і гроші лишаються в SQL, відгуки — у MongoDB.
/// </summary>
public class MongoReviewStore : IReviewStore
{
    public const string CollectionName = "reviews";

    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<Review> _reviews;
    private readonly string _name;

    public MongoReviewStore(IMongoDatabase db, WriteConcern? writeConcern = null, string name = "MongoDB")
    {
        _db = db;
        _name = name;
        var col = db.GetCollection<Review>(CollectionName);
        _reviews = writeConcern == null ? col : col.WithWriteConcern(writeConcern);
    }

    public string Name => _name;

    public IMongoCollection<Review> Collection => _reviews;

    /// <summary>Індекси, еквівалентні SQL-індексам із Database/Lab2-Reviews.sql.</summary>
    public async Task EnsureIndexesAsync()
    {
        var k = Builders<Review>.IndexKeys;
        await _reviews.Indexes.CreateManyAsync(
        [
            // ~ IX_Reviews_Restaurant_Created
            new CreateIndexModel<Review>(k.Ascending(r => r.RestaurantId).Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "restaurant_createdAt" }),
            // ~ IX_ReviewTags_Tag (multikey-індекс по масиву tags)
            new CreateIndexModel<Review>(k.Ascending(r => r.RestaurantId).Ascending(r => r.Tags).Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "restaurant_tags_createdAt" }),
            // ~ IX_ReviewDishRatings_MenuItem
            new CreateIndexModel<Review>(k.Ascending("dishes.menuItemId"),
                new CreateIndexOptions { Name = "dishes_menuItemId" }),
        ]);
    }

    public Task PingAsync() => _db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

    // ------------------------------------------------------------------ READ

    public async Task<Review?> GetByIdAsync(int id) =>
        await _reviews.Find(r => r.Id == id).FirstOrDefaultAsync();

    public Task<List<Review>> GetLatestForRestaurantAsync(int restaurantId, int take) =>
        _reviews.Find(r => r.RestaurantId == restaurantId)
            .SortByDescending(r => r.CreatedAt)
            .Limit(take)
            .ToListAsync();

    public Task<List<Review>> FindByTagAsync(int restaurantId, string tag, int take) =>
        _reviews.Find(r => r.RestaurantId == restaurantId && r.Tags.Contains(tag))
            .SortByDescending(r => r.CreatedAt)
            .Limit(take)
            .ToListAsync();

    // ------------------------------------------------------------------ AGGREGATES

    public async Task<List<DishStat>> GetDishStatsAsync(int restaurantId)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("restaurantId", restaurantId)),
            new BsonDocument("$unwind", "$dishes"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$dishes.menuItemId" },
                { "name", new BsonDocument("$first", "$dishes.name") },
                { "cnt", new BsonDocument("$sum", 1) },
                { "avg", new BsonDocument("$avg", "$dishes.rating") }
            }),
            new BsonDocument("$sort", new BsonDocument { { "avg", -1 }, { "_id", 1 } })
        };
        var docs = await _reviews.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return docs.Select(d => new DishStat(d["_id"].AsInt32, d["name"].AsString, d["cnt"].AsInt32, d["avg"].ToDouble())).ToList();
    }

    public async Task<List<AspectStat>> GetAspectStatsAsync(int restaurantId)
    {
        // aspects — це об'єкт з довільними ключами, тому перетворюємо його на масив пар k/v
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("restaurantId", restaurantId)),
            new BsonDocument("$project", new BsonDocument("a", new BsonDocument("$objectToArray", "$aspects"))),
            new BsonDocument("$unwind", "$a"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$a.k" },
                { "cnt", new BsonDocument("$sum", 1) },
                { "avg", new BsonDocument("$avg", "$a.v") }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };
        var docs = await _reviews.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return docs.Select(d => new AspectStat(d["_id"].AsString, d["cnt"].AsInt32, d["avg"].ToDouble())).ToList();
    }

    public async Task<List<RestaurantStat>> GetRestaurantStatsAsync()
    {
        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$restaurantId" },
                { "cnt", new BsonDocument("$sum", 1) },
                { "avg", new BsonDocument("$avg", "$rating") }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };
        var docs = await _reviews.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return docs.Select(d => new RestaurantStat(d["_id"].AsInt32, d["cnt"].AsInt32, d["avg"].ToDouble())).ToList();
    }

    // ------------------------------------------------------------------ WRITE

    public Task InsertAsync(Review review) => _reviews.InsertOneAsync(review);

    public Task InsertManyAsync(IReadOnlyList<Review> reviews) =>
        _reviews.InsertManyAsync(reviews, new InsertManyOptions { IsOrdered = false });

    public Task SetReplyAsync(int reviewId, ReviewReply reply) =>
        _reviews.UpdateOneAsync(r => r.Id == reviewId, Builders<Review>.Update.Set(r => r.Reply, reply));

    // ------------------------------------------------------------------ SERVICE

    public Task<long> CountAsync() => _reviews.CountDocumentsAsync(FilterDefinition<Review>.Empty);

    public Task DeleteAboveAsync(int maxId) => _reviews.DeleteManyAsync(r => r.Id > maxId);
}
