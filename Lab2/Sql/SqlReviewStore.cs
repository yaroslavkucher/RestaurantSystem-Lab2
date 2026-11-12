using System.Data;

using Dapper;
using Microsoft.Data.SqlClient;
using RestaurantSystem.Lab2.Models;

namespace RestaurantSystem.Lab2.Sql;

/// <summary>
/// Реляційна реалізація: один відгук "розкладено" по 6 таблицях (+ довідники Tags, MenuItems).
/// Усі читання повного агрегату виконуються ОДНИМ round-trip (batch з кількома result set-ами),
/// а запис — ОДНИМ batch-ем у транзакції. Тобто SQL-сторона написана за best practice,
/// а не "наївно" (N+1 запитів), щоб порівняння було чесним.
/// </summary>
public class SqlReviewStore : IReviewStore
{
    private readonly string _cs;
    private Dictionary<string, int>? _tagIds;

    public SqlReviewStore(string connectionString, bool delayedDurability = false)
    {
        _cs = connectionString;
        DelayedDurability = delayedDurability;
    }

    /// <summary>
    /// true => COMMIT не чекає скидання журналу на диск (аналог MongoDB w:1 без j:true).
    /// false => звичайний повністю durable COMMIT (аналог MongoDB j:true).
    /// </summary>
    public bool DelayedDurability { get; }

    public string Name => DelayedDurability ? "MS SQL (delayed dur.)" : "MS SQL";

    /// <summary>Вмикає/вимикає DELAYED_DURABILITY на рівні БД відповідно до режиму цього сховища.</summary>
    public async Task ApplyDurabilityModeAsync()
    {
        await using var c = Open();
        await c.ExecuteAsync($"ALTER DATABASE CURRENT SET DELAYED_DURABILITY = {(DelayedDurability ? "FORCED" : "DISABLED")}");
    }

    private SqlConnection Open()
    {
        var c = new SqlConnection(_cs);
        c.Open();
        return c;
    }

    public async Task PingAsync()
    {
        await using var c = Open();
        await c.ExecuteScalarAsync<int>("SELECT 1");
    }

    // ------------------------------------------------------------------ READ

    // Шаблон: спершу відбираємо Id потрібних відгуків у табличну змінну,
    // далі одним batch-ем тягнемо корінь та всі дочірні таблиці.
    private const string LoadByIdsTail = """
        SELECT r.Id, r.RestaurantId, r.CustomerId, r.OrderId, r.CreatedAt, r.Rating, r.Comment,
               r.VisitGuests, r.VisitOccasion
        FROM Reviews r JOIN @ids i ON i.Id = r.Id
        ORDER BY r.CreatedAt DESC;

        SELECT a.ReviewId, a.Aspect, a.Score
        FROM ReviewAspectRatings a JOIN @ids i ON i.Id = a.ReviewId;

        SELECT rt.ReviewId, t.Name
        FROM ReviewTags rt JOIN @ids i ON i.Id = rt.ReviewId JOIN Tags t ON t.Id = rt.TagId;

        SELECT d.ReviewId, d.MenuItemId, m.Name, d.Rating, d.Comment
        FROM ReviewDishRatings d JOIN @ids i ON i.Id = d.ReviewId JOIN MenuItems m ON m.Id = d.MenuItemId;

        SELECT p.ReviewId, p.Url, p.Caption
        FROM ReviewPhotos p JOIN @ids i ON i.Id = p.ReviewId
        ORDER BY p.Id;

        SELECT rr.ReviewId, rr.UserId, rr.Text, rr.CreatedAt
        FROM ReviewReplies rr JOIN @ids i ON i.Id = rr.ReviewId;
        """;

    private async Task<List<Review>> LoadAsync(string selectIdsSql, object param)
    {
        var sql = "SET NOCOUNT ON; DECLARE @ids TABLE (Id INT PRIMARY KEY);\n"
                  + "INSERT INTO @ids (Id) " + selectIdsSql + ";\n" + LoadByIdsTail;

        await using var c = Open();
        using var multi = await c.QueryMultipleAsync(sql, param);

        var reviews = (await multi.ReadAsync<ReviewRow>()).Select(r => r.ToModel()).ToList();
        var byId = reviews.ToDictionary(r => r.Id);

        foreach (var a in await multi.ReadAsync<AspectRow>())
            byId[a.ReviewId].Aspects[a.Aspect] = a.Score;
        foreach (var t in await multi.ReadAsync<TagRow>())
            byId[t.ReviewId].Tags.Add(t.Name);
        foreach (var d in await multi.ReadAsync<DishRow>())
            byId[d.ReviewId].Dishes.Add(new DishRating { MenuItemId = d.MenuItemId, Name = d.Name, Rating = d.Rating, Comment = d.Comment });
        foreach (var p in await multi.ReadAsync<PhotoRow>())
            byId[p.ReviewId].Photos.Add(new ReviewPhoto { Url = p.Url, Caption = p.Caption });
        foreach (var rr in await multi.ReadAsync<ReplyRow>())
            byId[rr.ReviewId].Reply = new ReviewReply { UserId = rr.UserId, Text = rr.Text, CreatedAt = DateTime.SpecifyKind(rr.CreatedAt, DateTimeKind.Utc) };

        return reviews;
    }

    public async Task<Review?> GetByIdAsync(int id) =>
        (await LoadAsync("SELECT Id FROM Reviews WHERE Id = @Id", new { Id = id })).FirstOrDefault();

    public Task<List<Review>> GetLatestForRestaurantAsync(int restaurantId, int take) =>
        LoadAsync("SELECT TOP (@Take) Id FROM Reviews WHERE RestaurantId = @R ORDER BY CreatedAt DESC",
            new { R = restaurantId, Take = take });

    public Task<List<Review>> FindByTagAsync(int restaurantId, string tag, int take) =>
        LoadAsync("""
            SELECT TOP (@Take) r.Id
            FROM Reviews r
            JOIN ReviewTags rt ON rt.ReviewId = r.Id
            JOIN Tags t ON t.Id = rt.TagId
            WHERE r.RestaurantId = @R AND t.Name = @Tag
            ORDER BY r.CreatedAt DESC
            """, new { R = restaurantId, Tag = tag, Take = take });

    // ------------------------------------------------------------------ AGGREGATES

    public async Task<List<DishStat>> GetDishStatsAsync(int restaurantId)
    {
        await using var c = Open();
        var rows = await c.QueryAsync<DishStatRow>("""
            SELECT d.MenuItemId, m.Name, COUNT(*) AS ReviewsCount, AVG(CAST(d.Rating AS FLOAT)) AS AvgRating
            FROM ReviewDishRatings d
            JOIN Reviews r   ON r.Id = d.ReviewId
            JOIN MenuItems m ON m.Id = d.MenuItemId
            WHERE r.RestaurantId = @R
            GROUP BY d.MenuItemId, m.Name
            ORDER BY AvgRating DESC, d.MenuItemId
            """, new { R = restaurantId });
        return rows.Select(x => new DishStat(x.MenuItemId, x.Name, x.ReviewsCount, x.AvgRating)).ToList();
    }

    public async Task<List<AspectStat>> GetAspectStatsAsync(int restaurantId)
    {
        await using var c = Open();
        var rows = await c.QueryAsync<AspectStatRow>("""
            SELECT a.Aspect, COUNT(*) AS Cnt, AVG(CAST(a.Score AS FLOAT)) AS AvgScore
            FROM ReviewAspectRatings a
            JOIN Reviews r ON r.Id = a.ReviewId
            WHERE r.RestaurantId = @R
            GROUP BY a.Aspect
            ORDER BY a.Aspect
            """, new { R = restaurantId });
        return rows.Select(x => new AspectStat(x.Aspect, x.Cnt, x.AvgScore)).ToList();
    }

    public async Task<List<RestaurantStat>> GetRestaurantStatsAsync()
    {
        await using var c = Open();
        var rows = await c.QueryAsync<RestaurantStatRow>("""
            SELECT RestaurantId, COUNT(*) AS ReviewsCount, AVG(CAST(Rating AS FLOAT)) AS AvgRating
            FROM Reviews
            GROUP BY RestaurantId
            ORDER BY RestaurantId
            """);
        return rows.Select(x => new RestaurantStat(x.RestaurantId, x.ReviewsCount, x.AvgRating)).ToList();
    }

    // ------------------------------------------------------------------ WRITE

    private async Task<Dictionary<string, int>> TagIdsAsync(SqlConnection c)
    {
        if (_tagIds != null) return _tagIds;
        var rows = await c.QueryAsync<(int Id, string Name)>("SELECT Id, Name FROM Tags");
        return _tagIds = rows.ToDictionary(x => x.Name, x => x.Id);
    }

    private sealed record ReviewTables(DataTable Reviews, DataTable Aspects, DataTable Tags, DataTable Dishes, DataTable Photos, DataTable Replies);

    /// <summary>Розкладає агрегати Review на 6 "плоских" таблиць (так само, як вони лежать у БД).</summary>
    private static ReviewTables ToTables(IEnumerable<Review> reviews, Dictionary<string, int> tagIds)
    {
        var t = new ReviewTables(
            Table(("Id", typeof(int)), ("RestaurantId", typeof(int)), ("CustomerId", typeof(int)), ("OrderId", typeof(int)),
                  ("CreatedAt", typeof(DateTime)), ("Rating", typeof(int)), ("Comment", typeof(string)),
                  ("VisitGuests", typeof(int)), ("VisitOccasion", typeof(string))),
            Table(("ReviewId", typeof(int)), ("Aspect", typeof(string)), ("Score", typeof(int))),
            Table(("ReviewId", typeof(int)), ("TagId", typeof(int))),
            Table(("ReviewId", typeof(int)), ("MenuItemId", typeof(int)), ("Rating", typeof(int)), ("Comment", typeof(string))),
            Table(("ReviewId", typeof(int)), ("Url", typeof(string)), ("Caption", typeof(string))),
            Table(("ReviewId", typeof(int)), ("UserId", typeof(int)), ("Text", typeof(string)), ("CreatedAt", typeof(DateTime))));

        foreach (var r in reviews)
        {
            t.Reviews.Rows.Add(r.Id, r.RestaurantId, r.CustomerId, (object?)r.OrderId ?? DBNull.Value, r.CreatedAt, r.Rating,
                (object?)r.Comment ?? DBNull.Value, (object?)r.Visit?.Guests ?? DBNull.Value, (object?)r.Visit?.Occasion ?? DBNull.Value);
            foreach (var a in r.Aspects) t.Aspects.Rows.Add(r.Id, a.Key, a.Value);
            foreach (var tag in r.Tags) t.Tags.Rows.Add(r.Id, tagIds[tag]);
            foreach (var d in r.Dishes) t.Dishes.Rows.Add(r.Id, d.MenuItemId, d.Rating, (object?)d.Comment ?? DBNull.Value);
            foreach (var ph in r.Photos) t.Photos.Rows.Add(r.Id, ph.Url, (object?)ph.Caption ?? DBNull.Value);
            if (r.Reply != null) t.Replies.Rows.Add(r.Id, r.Reply.UserId, r.Reply.Text, r.Reply.CreatedAt);
        }
        return t;
    }

    /// <summary>
    /// Вставка одного відгуку — через ту саму процедуру sp_InsertReviews (пакет з 1 елемента).
    /// </summary>
    public Task InsertAsync(Review review) => InsertManyAsync([review]);

    /// <summary>
    /// Вставка через збережену процедуру sp_InsertReviews з табличними параметрами (TVP):
    /// 1 round-trip, 1 транзакція, план виконання кешується (не залежить від кількості тегів/страв/фото).
    /// </summary>
    public async Task InsertManyAsync(IReadOnlyList<Review> reviews)
    {
        await using var c = Open();
        var t = ToTables(reviews, await TagIdsAsync(c));
        await c.ExecuteAsync("sp_InsertReviews", new
        {
            Reviews = t.Reviews.AsTableValuedParameter("dbo.ReviewTvp"),
            Aspects = t.Aspects.AsTableValuedParameter("dbo.ReviewAspectTvp"),
            Tags = t.Tags.AsTableValuedParameter("dbo.ReviewTagTvp"),
            Dishes = t.Dishes.AsTableValuedParameter("dbo.ReviewDishTvp"),
            Photos = t.Photos.AsTableValuedParameter("dbo.ReviewPhotoTvp"),
            Replies = t.Replies.AsTableValuedParameter("dbo.ReviewReplyTvp")
        }, commandType: CommandType.StoredProcedure, commandTimeout: 600);
    }

    public async Task SetReplyAsync(int reviewId, ReviewReply reply)
    {
        await using var c = Open();
        await c.ExecuteAsync("""
            SET NOCOUNT ON;
            UPDATE ReviewReplies SET UserId = @U, Text = @T, CreatedAt = @A WHERE ReviewId = @R;
            IF @@ROWCOUNT = 0
                INSERT INTO ReviewReplies (ReviewId, UserId, Text, CreatedAt) VALUES (@R, @U, @T, @A);
            """, new { R = reviewId, U = reply.UserId, T = reply.Text, A = reply.CreatedAt });
    }

    /// <summary>
    /// Початкове завантаження (seed): SqlBulkCopy у кожну з 6 таблиць в одній транзакції.
    /// </summary>
    public async Task BulkLoadAsync(IReadOnlyList<Review> reviews)
    {
        await using var c = Open();
        var t = ToTables(reviews, await TagIdsAsync(c));

        await using var tx = c.BeginTransaction();
        await Bulk(c, tx, "Reviews", t.Reviews, SqlBulkCopyOptions.KeepIdentity);
        await Bulk(c, tx, "ReviewAspectRatings", t.Aspects);
        await Bulk(c, tx, "ReviewTags", t.Tags);
        await Bulk(c, tx, "ReviewDishRatings", t.Dishes);
        await Bulk(c, tx, "ReviewPhotos", t.Photos);
        await Bulk(c, tx, "ReviewReplies", t.Replies);
        await tx.CommitAsync();
    }


    internal static DataTable Table(params (string Name, Type Type)[] cols)
    {
        var t = new DataTable();
        foreach (var (n, ty) in cols) t.Columns.Add(n, ty);
        return t;
    }

    internal static async Task Bulk(SqlConnection c, SqlTransaction? tx, string table, DataTable data,
        SqlBulkCopyOptions options = SqlBulkCopyOptions.Default)
    {
        if (data.Rows.Count == 0) return;
        using var bulk = new SqlBulkCopy(c, options | SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = table,
            BatchSize = 10_000,
            BulkCopyTimeout = 600
        };
        foreach (DataColumn col in data.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(data);
    }

    // ------------------------------------------------------------------ SERVICE

    public async Task<long> CountAsync()
    {
        await using var c = Open();
        return await c.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM Reviews");
    }

    public async Task DeleteAboveAsync(int maxId)
    {
        await using var c = Open();
        await c.ExecuteAsync("""
            DELETE FROM ReviewReplies       WHERE ReviewId > @M;
            DELETE FROM ReviewPhotos        WHERE ReviewId > @M;
            DELETE FROM ReviewDishRatings   WHERE ReviewId > @M;
            DELETE FROM ReviewTags          WHERE ReviewId > @M;
            DELETE FROM ReviewAspectRatings WHERE ReviewId > @M;
            DELETE FROM Reviews             WHERE Id > @M;
            """, new { M = maxId }, commandTimeout: 600);
    }

    // ------------------------------------------------------------------ ROW TYPES

    private class ReviewRow
    {
        public int Id { get; set; }
        public int RestaurantId { get; set; }
        public int CustomerId { get; set; }
        public int? OrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public int? VisitGuests { get; set; }
        public string? VisitOccasion { get; set; }

        public Review ToModel() => new()
        {
            Id = Id, RestaurantId = RestaurantId, CustomerId = CustomerId, OrderId = OrderId,
            CreatedAt = DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc), Rating = Rating, Comment = Comment,
            Visit = VisitGuests.HasValue ? new VisitInfo { Guests = VisitGuests.Value, Occasion = VisitOccasion } : null
        };
    }

    private class AspectRow { public int ReviewId { get; set; } public string Aspect { get; set; } = ""; public int Score { get; set; } }
    private class TagRow { public int ReviewId { get; set; } public string Name { get; set; } = ""; }
    private class DishRow { public int ReviewId { get; set; } public int MenuItemId { get; set; } public string Name { get; set; } = ""; public int Rating { get; set; } public string? Comment { get; set; } }
    private class PhotoRow { public int ReviewId { get; set; } public string Url { get; set; } = ""; public string? Caption { get; set; } }
    private class ReplyRow { public int ReviewId { get; set; } public int UserId { get; set; } public string Text { get; set; } = ""; public DateTime CreatedAt { get; set; } }
    private class DishStatRow { public int MenuItemId { get; set; } public string Name { get; set; } = ""; public int ReviewsCount { get; set; } public double AvgRating { get; set; } }
    private class AspectStatRow { public string Aspect { get; set; } = ""; public int Cnt { get; set; } public double AvgScore { get; set; } }
    private class RestaurantStatRow { public int RestaurantId { get; set; } public int ReviewsCount { get; set; } public double AvgRating { get; set; } }
}
