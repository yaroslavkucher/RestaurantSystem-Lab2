using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace RestaurantSystem.Lab2.Redis;

/// <summary>
/// Завдання 6: NoSQL іншого типу — key-value сховище Redis.
/// Для ресторанного бізнесу Redis закриває "оперативні", короткоживучі дані,
/// які не потрібно зберігати в SQL роками, але потрібно читати/писати за мікросекунди.
/// Кожен блок — окрема структура даних Redis і відповідні базові команди.
/// </summary>
public static class RedisDemo
{
    private const int RestaurantId = 1;

    public static async Task RunAsync()
    {
        using var mux = await ConnectionMultiplexer.ConnectAsync(Settings.Redis);
        var db = mux.GetDatabase();
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        Console.WriteLine($"== Redis {server.Version} ==\n");

        await MenuCacheAsync(db);
        await TableStatusAsync(db);
        await KitchenQueueAsync(db);
        await PopularDishesAsync(db);
        await SessionsAndCountersAsync(db);
        await TransactionAsync(db);
        await PubSubAsync(mux);
        await ScanKeysAsync(server, db);
    }

    // ---------------------------------------------------------------- STRING + TTL: кеш меню (cache-aside)
    private static async Task MenuCacheAsync(IDatabase db)
    {
        Console.WriteLine("1) STRING + TTL — кеш активного меню (cache-aside поверх SQL)");
        string key = $"menu:active";
        await db.KeyDeleteAsync(key);

        async Task<string> GetMenuAsync()
        {
            var cached = await db.StringGetAsync(key);                            // GET menu:active
            if (cached.HasValue) return cached!;

            await using var c = new SqlConnection(Settings.Sql);
            var items = await c.QueryAsync("SELECT Id, CategoryId, Name, Price FROM MenuItems WHERE IsDeleted = 0");
            var json = JsonSerializer.Serialize(items);
            await db.StringSetAsync(key, json, TimeSpan.FromMinutes(5));         // SET menu:active <json> EX 300
            return json;
        }

        var sw = Stopwatch.StartNew();
        var first = await GetMenuAsync();
        Console.WriteLine($"   cache MISS -> читаємо з SQL і кладемо в Redis: {sw.Elapsed.TotalMilliseconds:F2} ms, {first.Length / 1024.0:F1} KB");
        sw.Restart();
        await GetMenuAsync();
        Console.WriteLine($"   cache HIT  -> з Redis: {sw.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"   TTL {key} = {(await db.KeyTimeToLiveAsync(key))?.TotalSeconds:F0} c");     // TTL menu:active

        // Порівняння на 1000 запитах: SQL кожного разу vs Redis-кеш
        const int n = 1000;
        await using (var c = new SqlConnection(Settings.Sql))
        {
            await c.OpenAsync();
            sw.Restart();
            for (int i = 0; i < n; i++)
                await c.QueryAsync("SELECT Id, CategoryId, Name, Price FROM MenuItems WHERE IsDeleted = 0");
            var sqlMs = sw.Elapsed.TotalMilliseconds / n;
            sw.Restart();
            for (int i = 0; i < n; i++) await db.StringGetAsync(key);
            var redisMs = sw.Elapsed.TotalMilliseconds / n;
            Console.WriteLine($"   середнє на {n} запитах: SQL {sqlMs:F3} ms vs Redis {redisMs:F3} ms (x{sqlMs / redisMs:F1})");
        }

        // Інвалідація кешу при зміні ціни (те, що робить sp_UpdateMenuItemPrice у ЛР1)
        await db.KeyDeleteAsync(key);                                             // DEL menu:active
        Console.WriteLine($"   після зміни ціни: DEL {key} -> EXISTS = {await db.KeyExistsAsync(key)}\n");
    }

    // ---------------------------------------------------------------- HASH: стан столиків у реальному часі
    private static async Task TableStatusAsync(IDatabase db)
    {
        Console.WriteLine("2) HASH — поточний стан столиків ресторану");
        string key = $"restaurant:{RestaurantId}:tables";
        await db.KeyDeleteAsync(key);

        // HSET restaurant:1:tables 1 free 2 free ...
        await db.HashSetAsync(key, Enumerable.Range(1, 8).Select(t => new HashEntry(t, "free")).ToArray());
        await db.HashSetAsync(key, 3, "occupied");                                // HSET ... 3 occupied
        await db.HashSetAsync(key, 5, "reserved");
        await db.HashSetAsync(key, 6, "occupied");

        var all = await db.HashGetAllAsync(key);                                  // HGETALL
        Console.WriteLine("   HGETALL: " + string.Join(", ", all.Select(e => $"#{e.Name}={e.Value}")));
        Console.WriteLine($"   HGET стіл 3: {await db.HashGetAsync(key, 3)}");
        Console.WriteLine($"   вільних: {all.Count(e => e.Value == "free")} з {await db.HashLengthAsync(key)}\n");   // HLEN
    }

    // ---------------------------------------------------------------- LIST: черга замовлень на кухню (FIFO)
    private static async Task KitchenQueueAsync(IDatabase db)
    {
        Console.WriteLine("3) LIST — черга замовлень на кухню (FIFO)");
        string key = $"restaurant:{RestaurantId}:kitchen:queue";
        await db.KeyDeleteAsync(key);

        string[] tickets =
        [
            """{"orderId":101,"table":3,"items":["Борщ класичний","Вареники домашній"]}""",
            """{"orderId":102,"table":6,"items":["Стейк від шефа"]}""",
            """{"orderId":103,"table":1,"items":["Чізкейк класичний","Капучино класичний"]}"""
        ];
        foreach (var t in tickets) await db.ListLeftPushAsync(key, t);           // LPUSH (офіціант додає)

        Console.WriteLine($"   LLEN = {await db.ListLengthAsync(key)}");
        Console.WriteLine("   LRANGE 0 -1: " + string.Join(" | ", (await db.ListRangeAsync(key)).Select(v => v.ToString()[..18] + "…")));
        var next = await db.ListRightPopAsync(key);                               // RPOP (кухар бере найстаріше)
        Console.WriteLine($"   кухар взяв (RPOP): {next}");
        Console.WriteLine($"   залишилось: {await db.ListLengthAsync(key)}\n");
    }

    // ---------------------------------------------------------------- SORTED SET: топ популярних страв
    private static async Task PopularDishesAsync(IDatabase db)
    {
        Console.WriteLine("4) SORTED SET — рейтинг популярних страв за сьогодні");
        string key = $"restaurant:{RestaurantId}:popular:{DateTime.Today:yyyy-MM-dd}";
        await db.KeyDeleteAsync(key);

        var rnd = new Random(5);
        var dishes = Enumerable.Range(1, 15).Select(DataGenerator.MenuItemName).ToArray();
        for (int i = 0; i < 500; i++)                                             // кожна позиція замовлення
            await db.SortedSetIncrementAsync(key, dishes[(int)Math.Floor(Math.Pow(rnd.NextDouble(), 2) * dishes.Length)], 1); // ZINCRBY
        await db.KeyExpireAsync(key, TimeSpan.FromDays(2));                       // EXPIRE — старі дні зникають самі

        var top = await db.SortedSetRangeByRankWithScoresAsync(key, 0, 4, Order.Descending); // ZREVRANGE 0 4 WITHSCORES
        foreach (var (e, i) in top.Select((e, i) => (e, i)))
            Console.WriteLine($"   {i + 1}. {e.Element,-28} {e.Score} шт.");
        var rank = await db.SortedSetRankAsync(key, dishes[10], Order.Descending);  // ZREVRANK
        Console.WriteLine($"   місце '{dishes[10]}' у рейтингу: {rank + 1}\n");
    }

    // ---------------------------------------------------------------- STRING з TTL та INCR: сесії і лічильники
    private static async Task SessionsAndCountersAsync(IDatabase db)
    {
        Console.WriteLine("5) STRING + EXPIRE / INCR — сесія офіціанта і rate-limit");
        string token = Guid.NewGuid().ToString("N")[..12];
        string sKey = $"session:{token}";
        await db.StringSetAsync(sKey, """{"userId":2,"role":"Офіціант","restaurantId":1}""", TimeSpan.FromHours(8)); // SET .. EX 28800
        Console.WriteLine($"   SET {sKey} … EX 28800 -> GET: {await db.StringGetAsync(sKey)}");
        Console.WriteLine($"   TTL: {(await db.KeyTimeToLiveAsync(sKey))?.TotalHours:F1} год");

        // Обмеження: не більше 5 бронювань з одного номера телефону за хвилину
        string rl = "ratelimit:reservation:+380671234567";
        await db.KeyDeleteAsync(rl);
        for (int i = 1; i <= 7; i++)
        {
            long cnt = await db.StringIncrementAsync(rl);                         // INCR
            if (cnt == 1) await db.KeyExpireAsync(rl, TimeSpan.FromMinutes(1));   // EXPIRE 60
            Console.Write(cnt <= 5 ? $"   спроба {i}: OK" : $"   спроба {i}: ВІДХИЛЕНО");
            Console.WriteLine();
        }
        await db.KeyDeleteAsync(sKey);
        Console.WriteLine();
    }

    // ---------------------------------------------------------------- MULTI/EXEC: атомарна зміна стану
    private static async Task TransactionAsync(IDatabase db)
    {
        Console.WriteLine("6) MULTI/EXEC — атомарно: посадити гостей за стіл + додати замовлення в чергу");
        var tx = db.CreateTransaction();
        string tablesKey = $"restaurant:{RestaurantId}:tables";
        tx.AddCondition(Condition.HashEqual(tablesKey, 2, "free"));                // WATCH-подібна умова
        _ = tx.HashSetAsync(tablesKey, 2, "occupied");
        _ = tx.ListLeftPushAsync($"restaurant:{RestaurantId}:kitchen:queue", """{"orderId":104,"table":2,"items":["Узвар класичний"]}""");
        bool ok = await tx.ExecuteAsync();
        Console.WriteLine($"   EXEC -> {(ok ? "виконано" : "відхилено (стіл вже зайнятий)")}; стіл 2 = {await db.HashGetAsync(tablesKey, 2)}");

        var tx2 = db.CreateTransaction();
        tx2.AddCondition(Condition.HashEqual(tablesKey, 2, "free"));
        _ = tx2.HashSetAsync(tablesKey, 2, "occupied");
        Console.WriteLine($"   повторна спроба зайняти стіл 2 -> {(await tx2.ExecuteAsync() ? "виконано" : "відхилено (стіл вже зайнятий)")}\n");
    }

    // ---------------------------------------------------------------- PUB/SUB: сповіщення "страва готова"
    private static async Task PubSubAsync(ConnectionMultiplexer mux)
    {
        Console.WriteLine("7) PUB/SUB — кухня сповіщає офіціантів, що замовлення готове");
        var sub = mux.GetSubscriber();
        var channel = RedisChannel.Literal($"restaurant:{RestaurantId}:order-ready");
        var received = new TaskCompletionSource<string>();
        await sub.SubscribeAsync(channel, (_, msg) => received.TrySetResult(msg!));   // SUBSCRIBE

        long receivers = await sub.PublishAsync(channel, """{"orderId":101,"table":3}""");  // PUBLISH
        var msg = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Console.WriteLine($"   PUBLISH -> отримувачів: {receivers}; офіціант отримав: {msg}\n");
        await sub.UnsubscribeAsync(channel);
    }

    private static async Task ScanKeysAsync(IServer server, IDatabase db)
    {
        Console.WriteLine("8) SCAN — усі ключі ресторану та їх типи");
        await foreach (var key in server.KeysAsync(pattern: $"restaurant:{RestaurantId}:*"))   // SCAN 0 MATCH restaurant:1:*
            Console.WriteLine($"   {key,-40} {await db.KeyTypeAsync(key)}");                  // TYPE
        Console.WriteLine();
    }
}
