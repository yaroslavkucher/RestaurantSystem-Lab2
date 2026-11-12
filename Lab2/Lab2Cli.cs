using RestaurantSystem.Lab2.Mongo;
using RestaurantSystem.Lab2.Redis;

namespace RestaurantSystem.Lab2;

public static class Lab2Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int Arg(string name, int def)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : def;
        }

        int sqlIdx = Array.IndexOf(args, "--sql");
        if (sqlIdx >= 0 && sqlIdx + 1 < args.Length) Settings.SqlTarget = args[sqlIdx + 1];
        Console.WriteLine($"[SQL target: {Settings.SqlTarget}]");

        switch (args[0])
        {
            case "seed":
                await Seeder.RunAsync(Arg("--size", 100_000));
                return 0;
            case "bench":
                await Benchmark.RunAsync(Arg("--scale", 1));
                return 0;
            case "sizes":
                await Seeder.PrintSizesAsync();
                return 0;
            case "mongo-demo":
                await MongoDemo.RunAsync();
                return 0;
            case "redis-demo":
                await RedisDemo.RunAsync();
                return 0;
            default:
                Console.WriteLine("""
                    Команди ЛР2:
                      seed [--size 100000]   створити RestaurantsLab2 (SQL) + restaurant.reviews (MongoDB) і заповнити даними
                      bench [--scale 1]      бенчмарк SQL vs MongoDB (scale множить кількість ітерацій)
                      sizes                  обсяг даних у SQL і MongoDB
                      mongo-demo             базові запити MongoDB
                      redis-demo             базові запити Redis
                    Опція --sql docker|localdb (за замовчуванням docker) — де розташований SQL Server.
                    Без аргументів запускається демо ЛР1.
                    """);
                return 1;
        }
    }
}
