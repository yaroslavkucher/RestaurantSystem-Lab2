using RestaurantSystem.Lab2.Models;

namespace RestaurantSystem.Lab2;

/// <summary>
/// Детермінований генератор тестових даних (фіксований seed => однакові дані в SQL і MongoDB
/// та відтворювані результати між запусками).
/// </summary>
public class DataGenerator
{
    public const int RestaurantsCount = 50;
    public const int UsersPerRestaurant = 2;
    public const int CustomersCount = 20_000;
    public const int MenuItemsCount = 300;

    public static readonly string[] Roles = ["Адміністратор", "Офіціант", "Кухар", "Менеджер"];
    public static readonly string[] Categories = ["Перші страви", "Основні страви", "Салати", "Десерти", "Напої", "Закуски"];

    public static readonly string[] AllTags =
    [
        "затишно", "смачно", "швидко", "повільно", "дорого", "недорого", "романтично", "для-дітей",
        "жива-музика", "тераса", "веган", "великі-порції", "малі-порції", "гучно", "чисто",
        "привітний-персонал", "бізнес-ланч", "гарний-вид", "паркінг", "pet-friendly"
    ];

    // Набір аспектів змінний: у кожного відгуку — своя підмножина (це і є "гнучка схема")
    public static readonly string[] AllAspects =
        ["food", "service", "ambience", "cleanliness", "speed", "price", "music", "parking"];

    private static readonly string[] Occasions = ["день народження", "ділова зустріч", "побачення", "сімейна вечеря", "з друзями"];

    private static readonly string[] BaseDishes =
    [
        "Борщ", "Вареники", "Деруни", "Голубці", "Котлета по-київськи", "Банош", "Стейк", "Паста",
        "Піца", "Різотто", "Салат Цезар", "Грецький салат", "Чізкейк", "Тірамісу", "Сирники", "Узвар",
        "Лимонад", "Капучино", "Бограч", "Солянка", "Шашлик", "Бургер", "Рамен", "Сушi сет", "Курча гриль"
    ];

    private static readonly string[] Variants =
        ["класичний", "домашній", "від шефа", "з грибами", "з куркою", "з телятиною", "вегетаріанський", "гострий", "фірмовий", "сезонний", "міні", "XL"];

    private static readonly string[] Phrases =
    [
        "Все дуже сподобалось", "Обслуговування на висоті", "Чекали замовлення досить довго",
        "Страви гарячі та смачні", "Ціни трохи завищені", "Чудова атмосфера", "Офіціант був неуважний",
        "Обов'язково повернемось", "Порції великі", "Музика заголосна", "Дуже чисто", "Десерт — шедевр",
        "Стейк пересмажений", "Рекомендую друзям", "Інтер'єр сучасний", "Не вистачало солі"
    ];

    private static readonly string[] Replies =
    [
        "Дякуємо за відгук! Чекаємо на вас знову.",
        "Перепрошуємо за незручності, ми вже працюємо над цим.",
        "Нам дуже приємно! Передамо подяку кухні.",
        "Дякуємо, врахуємо ваші побажання."
    ];

    public static readonly DateTime From = new(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int SpanMinutes = 2 * 365 * 24 * 60;

    private readonly Random _rnd;

    public DataGenerator(int seed = 42) => _rnd = new Random(seed);

    public static string MenuItemName(int menuItemId)
    {
        int i = menuItemId - 1;
        return $"{BaseDishes[i % BaseDishes.Length]} {Variants[i / BaseDishes.Length % Variants.Length]}";
    }

    public static int MenuItemCategory(int menuItemId) => (menuItemId - 1) % Categories.Length + 1;

    public static int AdminUserId(int restaurantId) => (restaurantId - 1) * UsersPerRestaurant + 1;

    private int Score(int around) => Math.Clamp(around + _rnd.Next(-1, 2), 1, 5);

    public Review MakeReview(int id) => MakeReview(id, _rnd.Next(1, RestaurantsCount + 1));

    public Review MakeReview(int id, int restaurantId)
    {
        // Рейтинг зміщений у бік 4-5 (як у реальних сервісах відгуків)
        int[] weights = [5, 8, 17, 35, 35];
        int roll = _rnd.Next(100), rating = 1;
        for (int acc = 0, i = 0; i < 5; i++) { acc += weights[i]; if (roll < acc) { rating = i + 1; break; } }

        var createdAt = From.AddMinutes(_rnd.Next(SpanMinutes)).AddMilliseconds(_rnd.Next(1000));

        var r = new Review
        {
            Id = id,
            RestaurantId = restaurantId,
            CustomerId = _rnd.Next(1, CustomersCount + 1),
            OrderId = _rnd.Next(100) < 60 ? _rnd.Next(1, 1_000_000) : null,
            CreatedAt = createdAt,
            Rating = rating,
            Comment = _rnd.Next(100) < 85
                ? string.Join(". ", Enumerable.Range(0, _rnd.Next(1, 4)).Select(_ => Phrases[_rnd.Next(Phrases.Length)])) + "."
                : null
        };

        // 2..5 аспектів із довільної підмножини
        foreach (var a in AllAspects.OrderBy(_ => _rnd.Next()).Take(_rnd.Next(2, 6)))
            r.Aspects[a] = Score(rating);

        // 0..4 тегів
        r.Tags.AddRange(AllTags.OrderBy(_ => _rnd.Next()).Take(_rnd.Next(0, 5)));

        // 1..4 страв
        foreach (var m in Enumerable.Range(0, _rnd.Next(1, 5)).Select(_ => _rnd.Next(1, MenuItemsCount + 1)).Distinct())
            r.Dishes.Add(new DishRating
            {
                MenuItemId = m,
                Name = MenuItemName(m),
                Rating = Score(rating),
                Comment = _rnd.Next(100) < 25 ? Phrases[_rnd.Next(Phrases.Length)] : null
            });

        // 30% відгуків мають 1..3 фото
        if (_rnd.Next(100) < 30)
            for (int i = 0, n = _rnd.Next(1, 4); i < n; i++)
                r.Photos.Add(new ReviewPhoto
                {
                    Url = $"https://cdn.restaurant.local/reviews/{id}/{i + 1}.jpg",
                    Caption = _rnd.Next(100) < 50 ? "Фото страви" : null
                });

        // 35% — є відповідь адміністрації
        if (_rnd.Next(100) < 35)
            r.Reply = MakeReply(restaurantId, createdAt.AddHours(_rnd.Next(1, 72)));

        // 50% — є інформація про візит
        if (_rnd.Next(100) < 50)
            r.Visit = new VisitInfo
            {
                Guests = _rnd.Next(1, 9),
                Occasion = _rnd.Next(100) < 60 ? Occasions[_rnd.Next(Occasions.Length)] : null
            };

        return r;
    }

    public ReviewReply MakeReply(int restaurantId, DateTime at) => new()
    {
        UserId = AdminUserId(restaurantId),
        Text = Replies[_rnd.Next(Replies.Length)],
        CreatedAt = DateTime.SpecifyKind(new DateTime(at.Ticks - at.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc)
    };

    public int NextRestaurant() => _rnd.Next(1, RestaurantsCount + 1);
    public int NextInt(int minInclusive, int maxExclusive) => _rnd.Next(minInclusive, maxExclusive);
    public string NextTag() => AllTags[_rnd.Next(AllTags.Length)];
}
