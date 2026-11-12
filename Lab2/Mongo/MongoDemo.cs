using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using RestaurantSystem.Lab2.Models;

namespace RestaurantSystem.Lab2.Mongo;

/// <summary>
/// Демонстрація базових операцій MongoDB на колекції reviews (для захисту):
/// приклад документа, CRUD, запити по вкладених полях, гнучка схема, explain-план.
/// </summary>
public static class MongoDemo
{
    public static async Task RunAsync()
    {
        var db = new MongoClient(Settings.Mongo).GetDatabase(Settings.MongoDatabase);
        var col = db.GetCollection<Review>(MongoReviewStore.CollectionName);
        var raw = db.GetCollection<BsonDocument>(MongoReviewStore.CollectionName);
        var json = new JsonWriterSettings { Indent = true, OutputMode = JsonOutputMode.RelaxedExtendedJson };

        Console.WriteLine("== MongoDB demo ==\n");

        Console.WriteLine("1) Приклад документа (той самий відгук у SQL займає рядки в 6 таблицях):");
        var sample = await raw.Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Exists("reply"),
                Builders<BsonDocument>.Filter.SizeGt("photos", 0)))
            .Limit(1).FirstAsync();
        Console.WriteLine(sample.ToJson(json));

        Console.WriteLine("\n2) Пошук по вкладеному полю масиву: відгуки, де страву #5 оцінили на 1");
        var f = Builders<Review>.Filter.ElemMatch(r => r.Dishes, d => d.MenuItemId == 5 && d.Rating == 1);
        Console.WriteLine($"   db.reviews.countDocuments({{dishes: {{$elemMatch: {{menuItemId: 5, rating: 1}}}}}}) = {await col.CountDocumentsAsync(f)}");

        Console.WriteLine("\n3) Гнучка схема: відгуки, в яких оцінено 'parking' на <= 2 (поле є не в усіх документах)");
        var pf = new BsonDocument("aspects.parking", new BsonDocument("$lte", 2));
        Console.WriteLine($"   db.reviews.countDocuments({pf}) = {await raw.CountDocumentsAsync(pf)}");

        Console.WriteLine("\n4) Додавання нового поля лише одному документу без ALTER TABLE");
        await raw.UpdateOneAsync(new BsonDocument("_id", 1),
            Builders<BsonDocument>.Update.Set("moderation", new BsonDocument { { "status", "approved" }, { "by", "auto" } }));
        Console.WriteLine("   " + (await raw.Find(new BsonDocument("_id", 1)).Project(new BsonDocument { { "moderation", 1 }, { "rating", 1 } }).FirstAsync()).ToJson());
        await raw.UpdateOneAsync(new BsonDocument("_id", 1), Builders<BsonDocument>.Update.Unset("moderation"));

        Console.WriteLine("\n5) $push у вкладений масив (додати фото до відгуку) і $pull назад");
        var photo = new ReviewPhoto { Url = "https://cdn.restaurant.local/demo.jpg", Caption = "demo" };
        await col.UpdateOneAsync(r => r.Id == 2, Builders<Review>.Update.Push(r => r.Photos, photo));
        Console.WriteLine($"   фото у відгуку #2: {(await col.Find(r => r.Id == 2).FirstAsync()).Photos.Count}");
        await col.UpdateOneAsync(r => r.Id == 2, Builders<Review>.Update.PullFilter(r => r.Photos, p => p.Url == photo.Url));

        Console.WriteLine("\n6) Топ-5 тегів по всій мережі ($unwind + $group + $sort + $limit)");
        var top = await raw.Aggregate()
            .Unwind("tags")
            .Group(new BsonDocument { { "_id", "$tags" }, { "n", new BsonDocument("$sum", 1) } })
            .Sort(new BsonDocument("n", -1)).Limit(5).ToListAsync();
        foreach (var t in top) Console.WriteLine($"   {t["_id"],-22} {t["n"]}");

        Console.WriteLine("\n7) explain() запиту 'останні 20 відгуків ресторану з тегом' — використовується індекс:");
        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", MongoReviewStore.CollectionName },
                    { "filter", new BsonDocument { { "restaurantId", 7 }, { "tags", "затишно" } } },
                    { "sort", new BsonDocument("createdAt", -1) },
                    { "limit", 20 }
                }
            },
            { "verbosity", "executionStats" }
        });
        var es = explain["executionStats"].AsBsonDocument;
        var winning = explain["queryPlanner"]["winningPlan"].ToJson();
        var idx = System.Text.RegularExpressions.Regex.Match(winning, "\"indexName\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
        Console.WriteLine($"   index = {idx}, docsExamined = {es["totalDocsExamined"]}, keysExamined = {es["totalKeysExamined"]}, nReturned = {es["nReturned"]}");
        Console.WriteLine();
    }
}
