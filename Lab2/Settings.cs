namespace RestaurantSystem.Lab2;

/// <summary>Налаштування підключень. Можна перевизначити змінними середовища.</summary>
public static class Settings
{
    public const string SqlDatabase = "RestaurantsLab2";

    /// <summary>
    /// "docker" (за замовчуванням) — SQL Server 2022 у Docker, як і MongoDB: однаковий мережевий шлях
    /// і однакові ресурси VM => чесне порівняння.
    /// "localdb" — (localdb)\MSSQLLocalDB з ЛР1 (shared memory, без мережі Docker).
    /// </summary>
    public static string SqlTarget { get; set; } = "docker";

    // Тестовий пароль локального dev-контейнера (див. docker-compose.yml)
    private const string DockerSql = "Server=localhost,14330;User Id=sa;Password=Lab2_Restaurant!Pass;TrustServerCertificate=True;";
    private const string LocalDbSql = "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True;";

    private static string SqlBase => SqlTarget == "localdb" ? LocalDbSql : DockerSql;

    public static string SqlMaster => SqlBase + "Database=master;";

    public static string Sql => SqlBase + $"Database={SqlDatabase};";

    public static string Mongo =>
        Environment.GetEnvironmentVariable("LAB2_MONGO") ?? "mongodb://localhost:27017";

    public const string MongoDatabase = "restaurant";

    public static string Redis =>
        Environment.GetEnvironmentVariable("LAB2_REDIS") ?? "localhost:6380";

    /// <summary>Шукає папку Database (зі скриптами ЛР1/ЛР2) вгору від поточної директорії.</summary>
    public static string DatabaseScriptsDir()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Database");
                if (File.Exists(Path.Combine(candidate, "init-db.sql"))) return candidate;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("Не знайдено папку Database з init-db.sql");
    }

    public static string ResultsDir()
    {
        var root = Directory.GetParent(DatabaseScriptsDir())!.FullName;
        var dir = Path.Combine(root, "Lab2", "results");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
