using System.Diagnostics;
using System.Globalization;
using System.Text;
using MongoDB.Driver;
using RestaurantSystem.Lab2.Models;
using RestaurantSystem.Lab2.Mongo;
using RestaurantSystem.Lab2.Sql;

namespace RestaurantSystem.Lab2;

/// <summary>
/// Методика:
///  * однакові дані в обох БД (детермінований генератор), однакові індекси;
///  * обидві сторони повертають ОДНАКОВИЙ повний об'єкт (перевіряється перед заміром);
///  * для кожного сценарію — прогрів (warmup), потім N ітерацій; вхідні параметри (id, ресторан, тег)
///    випадкові, але однакова послідовність для обох БД (той самий seed);
///  * рахуємо медіану, середнє, p95, min, max — медіана стійка до поодиноких викидів (GC, фонові процеси);
///  * порядок SQL/Mongo чергується між сценаріями, щоб жодна БД не мала систематичної переваги "другого запуску";
///  * базова лінія (SELECT 1 / ping) показує вартість самого round-trip — її варто віднімати подумки;
///  * для запису MongoDB міряється у двох режимах: за замовчуванням (w:1, без очікування журналу)
///    і з j:true (як SQL COMMIT — чекає запису журналу на диск).
/// </summary>
public class Benchmark
{
    private const int BenchIdBase = 100_000_000;

    private readonly List<BenchResult> _results = new();
    private readonly int _scale;
    private int _orderFlip;

    public Benchmark(int scale) => _scale = scale;

    public record BenchResult(string Scenario, string Db, int Iterations, double Median, double Mean, double P95, double Min, double Max);

    public static async Task RunAsync(int scale)
    {
        var sql = new SqlReviewStore(Settings.Sql);
        var mongoDb = new MongoClient(Settings.Mongo).GetDatabase(Settings.MongoDatabase);
        var mongo = new MongoReviewStore(mongoDb);
        var mongoJ = new MongoReviewStore(mongoDb, new WriteConcern(w: 1, journal: true), "MongoDB (j:true)");

        // Відгуки, які вставляють тести запису, мають Id >= BenchIdBase і видаляються після сценарію.
        // Якщо попередній запуск впав посеред тесту — прибираємо "хвости" ДО перевірки кількості.
        await sql.DeleteAboveAsync(BenchIdBase);
        await mongo.DeleteAboveAsync(BenchIdBase);

        long sqlCount = await sql.CountAsync(), mongoCount = await mongo.CountAsync();
        if (sqlCount == 0 || sqlCount != mongoCount)
            throw new InvalidOperationException($"Дані не збігаються або порожні: SQL={sqlCount}, Mongo={mongoCount}. Запустіть 'seed'.");

        int seedMax = (int)sqlCount;      // seed генерує Id 1..N без пропусків
        int baseMax = BenchIdBase;

        Console.WriteLine($"== Benchmark: {sqlCount:N0} відгуків у кожній БД, множник ітерацій x{scale} ==\n");

        await VerifySameResultsAsync(sql, mongo);

        var b = new Benchmark(scale);
        var stores = new IReviewStore[] { sql, mongo };

        await b.Scenario("0. Baseline round-trip (SELECT 1 / ping)", stores, 500, (s, _) => s.PingAsync());

        await b.Scenario("1. Відгук за Id (повний агрегат)", stores, 500,
            (s, g) => s.GetByIdAsync(g.NextInt(1, seedMax + 1)));

        await b.Scenario("2. 20 останніх відгуків ресторану", stores, 200,
            (s, g) => s.GetLatestForRestaurantAsync(g.NextRestaurant(), 20));

        await b.Scenario("3. 20 останніх відгуків ресторану з тегом", stores, 200,
            (s, g) => s.FindByTagAsync(g.NextRestaurant(), g.NextTag(), 20));

        await b.Scenario("4. Рейтинг страв ресторану (агрегація)", stores, 100,
            (s, g) => s.GetDishStatsAsync(g.NextRestaurant()));

        await b.Scenario("5. Середні оцінки за аспектами (агрегація)", stores, 100,
            (s, g) => s.GetAspectStatsAsync(g.NextRestaurant()));

        await b.Scenario("6. Рейтинг усіх ресторанів (full scan)", stores, 20,
            (s, _) => s.GetRestaurantStatsAsync());

        // --- Запис. Порівнюємо в однакових режимах надійності:
        //     MS SQL (durable COMMIT)            <-> MongoDB (j:true)
        //     MS SQL (DELAYED_DURABILITY=FORCED) <-> MongoDB (за замовчуванням, w:1)
        var sqlDelayed = new SqlReviewStore(Settings.Sql, delayedDurability: true);
        var writeStores = new IReviewStore[] { sql, sqlDelayed, mongo, mongoJ };

        static async Task Durability(IReviewStore s)
        {
            if (s is SqlReviewStore sq) await sq.ApplyDurabilityModeAsync();
        }
        Task ResetDurability(IReviewStore _) => sql.ApplyDurabilityModeAsync();

        await b.Scenario("7. Відповідь адміністрації (update)", writeStores, 300,
            (s, g) =>
            {
                int id = g.NextInt(1, seedMax + 1);
                return s.SetReplyAsync(id, g.MakeReply(g.NextRestaurant(), DateTime.UtcNow));
            },
            before: Durability, after: ResetDurability);

        // Вставки: кожна БД отримує однакові документи з однаковими Id, після сценарію — прибирання
        int nextId = baseMax;
        await b.Scenario("8. Вставка 1 відгуку (з усіма дочірніми)", writeStores, 300,
            (s, g) => s.InsertAsync(g.MakeReview(Interlocked.Increment(ref nextId))),
            before: async s => { nextId = baseMax; await Durability(s); },
            after: async s => { await s.DeleteAboveAsync(baseMax); await ResetDurability(s); });

        await b.Scenario("9. Пакетна вставка 1000 відгуків", writeStores, 10,
            (s, g) =>
            {
                var batch = Enumerable.Range(0, 1000).Select(_ => g.MakeReview(++nextId)).ToList();
                return s.InsertManyAsync(batch);
            },
            before: async s => { nextId = baseMax; await Durability(s); },
            after: async s => { await s.DeleteAboveAsync(baseMax); await ResetDurability(s); },
            warmup: 1);

        b.Print();
        await b.SaveAsync(sqlCount);
    }

    /// <summary>Перед заміром переконуємось, що обидві БД повертають однаковий результат.</summary>
    private static async Task VerifySameResultsAsync(IReviewStore a, IReviewStore b)
    {
        static string Key(Review? r) => r == null ? "null" :
            $"{r.Id}|{r.RestaurantId}|{r.CustomerId}|{r.OrderId}|{r.CreatedAt:O}|{r.Rating}|{r.Comment}|" +
            string.Join(",", r.Aspects.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")) + "|" +
            string.Join(",", r.Tags.Order()) + "|" +
            string.Join(",", r.Dishes.OrderBy(d => d.MenuItemId).Select(d => $"{d.MenuItemId}:{d.Name}:{d.Rating}:{d.Comment}")) + "|" +
            string.Join(",", r.Photos.Select(p => p.Url + p.Caption)) + "|" +
            (r.Reply == null ? "-" : $"{r.Reply.UserId}:{r.Reply.Text}") + "|" +
            (r.Visit == null ? "-" : $"{r.Visit.Guests}:{r.Visit.Occasion}");

        int mismatches = 0;
        var g = new DataGenerator(1);
        int total = (int)await a.CountAsync();
        for (int i = 0; i < 200; i++)
        {
            int id = g.NextInt(1, total + 1);
            if (Key(await a.GetByIdAsync(id)) != Key(await b.GetByIdAsync(id))) mismatches++;
        }
        var la = await a.GetLatestForRestaurantAsync(7, 20);
        var lb = await b.GetLatestForRestaurantAsync(7, 20);
        if (!la.Select(Key).SequenceEqual(lb.Select(Key))) mismatches++;

        var da = await a.GetDishStatsAsync(7);
        var db = await b.GetDishStatsAsync(7);
        if (da.Count != db.Count || da.Zip(db).Any(p => p.First.MenuItemId != p.Second.MenuItemId || Math.Abs(p.First.AvgRating - p.Second.AvgRating) > 1e-9))
            mismatches++;

        Console.WriteLine(mismatches == 0
            ? "Перевірка коректності: SQL і MongoDB повертають ідентичні дані (200 випадкових Id + сторінка + агрегація) ✓\n"
            : $"УВАГА: розбіжностей між БД: {mismatches}\n");
    }

    private async Task Scenario(string name, IReviewStore[] stores, int iterations,
        Func<IReviewStore, DataGenerator, Task> op,
        Func<IReviewStore, Task>? before = null, Func<IReviewStore, Task>? after = null, int warmup = 20)
    {
        iterations *= _scale;
        Console.WriteLine(name);

        var order = (_orderFlip++ % 2 == 0) ? stores : stores.Reverse().ToArray();
        foreach (var s in order)
        {
            if (before != null) await before(s);

            var warm = new DataGenerator(seed: 999);
            for (int i = 0; i < warmup; i++) await op(s, warm);

            if (after != null) await after(s);
            if (before != null) await before(s);

            var gen = new DataGenerator(seed: 2024); // однаковий набір параметрів для всіх БД
            var times = new double[iterations];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            for (int i = 0; i < iterations; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                await op(s, gen);
                times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            }

            if (after != null) await after(s);

            Array.Sort(times);
            var r = new BenchResult(name, s.Name, iterations,
                Median: Percentile(times, 0.5), Mean: times.Average(), P95: Percentile(times, 0.95),
                Min: times[0], Max: times[^1]);
            _results.Add(r);
            Console.WriteLine($"   {s.Name,-22} median {r.Median,9:F3} ms   p95 {r.P95,9:F3} ms   mean {r.Mean,9:F3} ms");
        }
    }

    private static double Percentile(double[] sorted, double p)
    {
        double idx = (sorted.Length - 1) * p;
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    private void Print()
    {
        Console.WriteLine("\n================ ПІДСУМОК (медіана, мс) ================");
        void Line(string label, BenchResult? s, BenchResult? m)
        {
            if (s == null || m == null) return;
            double ratio = s.Median / m.Median;
            string winner = ratio > 1 ? $"MongoDB швидше в {ratio:F2}x" : $"SQL швидше в {1 / ratio:F2}x";
            Console.WriteLine($"{label,-58} SQL {s.Median,9:F3} | Mongo {m.Median,9:F3} | {winner}");
        }

        foreach (var grp in _results.GroupBy(r => r.Scenario))
        {
            BenchResult? Db(string name) => grp.FirstOrDefault(r => r.Db == name);
            if (Db("MongoDB (j:true)") == null)
                Line(grp.Key, Db("MS SQL"), Db("MongoDB"));
            else
            {
                Line(grp.Key + " [durable]", Db("MS SQL"), Db("MongoDB (j:true)"));
                Line(grp.Key + " [fast ack]", Db("MS SQL (delayed dur.)"), Db("MongoDB"));
            }
        }
    }

    private async Task SaveAsync(long dataSize)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"# Результати бенчмарку ({DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine();
        sb.AppendLine($"SQL Server: **{Settings.SqlTarget}**. Обсяг: **{dataSize:N0}** відгуків у кожній БД. Час — мілісекунди на одну операцію (з боку клієнта, включно з мережею та десеріалізацією).");
        sb.AppendLine();
        sb.AppendLine("| Сценарій | БД | Ітерацій | Медіана | Середнє | p95 | Min | Max |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var r in _results)
            sb.AppendLine(string.Format(ci, "| {0} | {1} | {2} | {3:F3} | {4:F3} | {5:F3} | {6:F3} | {7:F3} |",
                r.Scenario, r.Db, r.Iterations, r.Median, r.Mean, r.P95, r.Min, r.Max));

        var path = Path.Combine(Settings.ResultsDir(), $"bench-{dataSize}-sql-{Settings.SqlTarget}.md");
        await File.WriteAllTextAsync(path, sb.ToString());

        var csv = new StringBuilder("scenario;db;iterations;median;mean;p95;min;max\n");
        foreach (var r in _results)
            csv.AppendLine(string.Format(ci, "{0};{1};{2};{3:F4};{4:F4};{5:F4};{6:F4};{7:F4}",
                r.Scenario, r.Db, r.Iterations, r.Median, r.Mean, r.P95, r.Min, r.Max));
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".csv"), csv.ToString());

        Console.WriteLine($"\nЗбережено: {path}");
    }
}
