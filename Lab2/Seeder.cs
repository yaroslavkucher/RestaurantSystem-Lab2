using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using MongoDB.Driver;
using RestaurantSystem.Lab2.Mongo;
using RestaurantSystem.Lab2.Sql;

namespace RestaurantSystem.Lab2;

/// <summary>
/// Створює з нуля SQL-базу RestaurantsLab2 (схема ЛР1 + таблиці відгуків ЛР2),
/// заповнює довідники і генерує однаковий набір відгуків у SQL та MongoDB.
/// </summary>
public static class Seeder
{
    public static async Task RunAsync(int size)
    {
        Console.WriteLine($"== Seed: {size:N0} відгуків ==");
        var sw = Stopwatch.StartNew();

        await RecreateSqlDatabaseAsync();
        await SeedSqlReferenceDataAsync();
        Console.WriteLine($"SQL: схема і довідники готові ({sw.Elapsed.TotalSeconds:F1} c)");

        var mongoClient = new MongoClient(Settings.Mongo);
        await mongoClient.DropDatabaseAsync(Settings.MongoDatabase);
        var mongo = new MongoReviewStore(mongoClient.GetDatabase(Settings.MongoDatabase));
        await mongo.EnsureIndexesAsync();

        var sql = new SqlReviewStore(Settings.Sql);
        var gen = new DataGenerator(seed: 42);

        const int batch = 5_000;
        TimeSpan sqlTime = TimeSpan.Zero, mongoTime = TimeSpan.Zero;
        for (int from = 1; from <= size; from += batch)
        {
            int to = Math.Min(size, from + batch - 1);
            var reviews = Enumerable.Range(from, to - from + 1).Select(id => gen.MakeReview(id)).ToList();

            var t = Stopwatch.StartNew();
            await sql.BulkLoadAsync(reviews);
            sqlTime += t.Elapsed;

            t.Restart();
            await mongo.InsertManyAsync(reviews);
            mongoTime += t.Elapsed;

            Console.Write($"\r  записано {to:N0}/{size:N0}");
        }
        Console.WriteLine();
        Console.WriteLine($"  час завантаження: SQL (SqlBulkCopy) {sqlTime.TotalSeconds:F1} c, MongoDB (insertMany) {mongoTime.TotalSeconds:F1} c");

        // Оновлюємо статистику, щоб оптимізатор SQL мав коректні плани
        await using (var c = new SqlConnection(Settings.Sql))
            await c.ExecuteAsync("EXEC sp_updatestats", commandTimeout: 600);

        await PrintSizesAsync();
        Console.WriteLine($"Готово за {sw.Elapsed.TotalSeconds:F1} c");
    }

    private static async Task RecreateSqlDatabaseAsync()
    {
        await using (var master = new SqlConnection(Settings.SqlMaster))
        {
            await master.ExecuteAsync($"""
                IF DB_ID('{Settings.SqlDatabase}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Settings.SqlDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Settings.SqlDatabase}];
                END
                CREATE DATABASE [{Settings.SqlDatabase}];
                ALTER DATABASE [{Settings.SqlDatabase}] SET RECOVERY SIMPLE;
                """, commandTimeout: 300);
        }
        SqlConnection.ClearAllPools();

        var dir = Settings.DatabaseScriptsDir();
        foreach (var file in new[] { "init-db.sql", "Indexes.sql", "Lab2-Reviews.sql" })
            await ExecuteScriptAsync(Path.Combine(dir, file));
    }

    private static async Task ExecuteScriptAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        var batches = Regex.Split(text, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        await using var c = new SqlConnection(Settings.Sql);
        await c.OpenAsync();
        foreach (var b in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
            await c.ExecuteAsync(b);
    }

    private static async Task SeedSqlReferenceDataAsync()
    {
        await using var c = new SqlConnection(Settings.Sql);
        await c.OpenAsync();

        var restaurants = SqlReviewStore.Table(("Id", typeof(int)), ("Name", typeof(string)), ("Address", typeof(string)), ("Phone", typeof(string)));
        for (int i = 1; i <= DataGenerator.RestaurantsCount; i++)
            restaurants.Rows.Add(i, $"Ресторан №{i}", $"м. Львів, вул. Тестова, {i}", $"+38032{i:D7}");

        var roles = SqlReviewStore.Table(("Id", typeof(int)), ("Name", typeof(string)));
        for (int i = 0; i < DataGenerator.Roles.Length; i++) roles.Rows.Add(i + 1, DataGenerator.Roles[i]);

        var users = SqlReviewStore.Table(("Id", typeof(int)), ("RestaurantId", typeof(int)), ("RoleId", typeof(int)),
            ("Username", typeof(string)), ("PasswordHash", typeof(string)), ("FullName", typeof(string)));
        for (int r = 1; r <= DataGenerator.RestaurantsCount; r++)
        {
            int admin = DataGenerator.AdminUserId(r);
            users.Rows.Add(admin, r, 1, $"admin{r}", "seed-hash", $"Адміністратор ресторану {r}");
            users.Rows.Add(admin + 1, r, 2, $"waiter{r}", "seed-hash", $"Офіціант ресторану {r}");
        }

        var customers = SqlReviewStore.Table(("Id", typeof(int)), ("FirstName", typeof(string)), ("LastName", typeof(string)),
            ("Phone", typeof(string)), ("Email", typeof(string)));
        for (int i = 1; i <= DataGenerator.CustomersCount; i++)
            customers.Rows.Add(i, $"Клієнт{i}", $"Прізвище{i}", $"+38067{i:D7}", $"client{i}@mail.test");

        var categories = SqlReviewStore.Table(("Id", typeof(int)), ("Name", typeof(string)));
        for (int i = 0; i < DataGenerator.Categories.Length; i++) categories.Rows.Add(i + 1, DataGenerator.Categories[i]);

        var menu = SqlReviewStore.Table(("Id", typeof(int)), ("CategoryId", typeof(int)), ("Name", typeof(string)), ("Price", typeof(decimal)));
        var rnd = new Random(7);
        for (int i = 1; i <= DataGenerator.MenuItemsCount; i++)
            menu.Rows.Add(i, DataGenerator.MenuItemCategory(i), DataGenerator.MenuItemName(i), Math.Round((decimal)(rnd.NextDouble() * 500 + 50), 2));

        var tags = SqlReviewStore.Table(("Id", typeof(int)), ("Name", typeof(string)));
        for (int i = 0; i < DataGenerator.AllTags.Length; i++) tags.Rows.Add(i + 1, DataGenerator.AllTags[i]);

        var keep = SqlBulkCopyOptions.KeepIdentity;
        await SqlReviewStore.Bulk(c, null, "Restaurants", restaurants, keep);
        await SqlReviewStore.Bulk(c, null, "Roles", roles, keep);
        await SqlReviewStore.Bulk(c, null, "Users", users, keep);
        await SqlReviewStore.Bulk(c, null, "Customers", customers, keep);
        await SqlReviewStore.Bulk(c, null, "MenuCategories", categories, keep);
        await SqlReviewStore.Bulk(c, null, "MenuItems", menu, keep);
        await SqlReviewStore.Bulk(c, null, "Tags", tags, keep);
    }

    public static async Task PrintSizesAsync()
    {
        Console.WriteLine("\nОб'єм даних:");
        await using (var c = new SqlConnection(Settings.Sql))
        {
            var rows = await c.QueryAsync<(string Name, long Rows, long ReservedKb)>("""
                SELECT t.name,
                       SUM(CASE WHEN p.index_id IN (0,1) THEN p.row_count ELSE 0 END),
                       SUM(p.reserved_page_count) * 8
                FROM sys.tables t
                JOIN sys.dm_db_partition_stats p ON p.object_id = t.object_id
                WHERE t.name IN ('Reviews','ReviewAspectRatings','ReviewTags','ReviewDishRatings','ReviewPhotos','ReviewReplies')
                GROUP BY t.name ORDER BY t.name
                """);
            long totalRows = 0, totalKb = 0;
            foreach (var r in rows)
            {
                Console.WriteLine($"  SQL  {r.Name,-22} {r.Rows,10:N0} рядків  {r.ReservedKb / 1024.0,8:F1} MB");
                totalRows += r.Rows; totalKb += r.ReservedKb;
            }
            Console.WriteLine($"  SQL  {"РАЗОМ (6 таблиць)",-22} {totalRows,10:N0} рядків  {totalKb / 1024.0,8:F1} MB (дані + індекси)");
        }

        var db = new MongoClient(Settings.Mongo).GetDatabase(Settings.MongoDatabase);
        var stats = await db.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("collStats", MongoReviewStore.CollectionName));
        double mb(string k) => stats[k].ToDouble() / 1024 / 1024;
        Console.WriteLine($"  Mongo reviews                {stats["count"].ToInt64(),10:N0} документів  " +
                          $"data {mb("size"):F1} MB, storage {mb("storageSize"):F1} MB (стиснено), indexes {mb("totalIndexSize"):F1} MB");
    }
}
