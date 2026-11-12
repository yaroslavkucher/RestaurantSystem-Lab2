# Restaurant System — Лабораторна робота №2 (NoSQL)

Продовження ЛР1 («Система управління ресторанним бізнесом», MS SQL + Repository/UnitOfWork).

* **MongoDB** (документна БД): відгуки клієнтів, колекція `reviews`. Реляційний аналог — 6 таблиць SQL.
* **Redis** (key-value): кеш меню, стан столиків, черга на кухню, топ страв, сесії, rate-limit, pub/sub.
* **Бенчмарк** SQL vs MongoDB на 10 тис. / 100 тис. / 1 млн відгуків.

Звіт з аргументацією, методикою та результатами: **Lab2/REPORT.docx**.

## Структура

| Шлях | Що там |
|---|---|
| `Database/*.sql` | схема ЛР1 + `Lab2-Reviews.sql` (таблиці відгуків, TVP, `sp_InsertReviews`) |
| `Database/mongo-queries.js` | базові запити MongoDB для `mongosh` |
| `Database/redis-commands.txt` | базові команди Redis для `redis-cli` |
| `Lab2/Models/Review.cs` | модель відгуку (документ MongoDB) |
| `Lab2/Sql/SqlReviewStore.cs` | реалізація на SQL Server |
| `Lab2/Mongo/MongoReviewStore.cs` | реалізація на MongoDB |
| `Lab2/Benchmark.cs` | тестування швидкодії |
| `Lab2/Redis/RedisDemo.cs` | Redis: базові структури та команди |
| `Lab2/results/` | результати вимірювань |

## Запуск

Потрібні .NET 10 SDK і Docker.

```bash
docker compose up -d                  # MongoDB :27017, Redis :6380, SQL Server :14330
dotnet run -- seed --size 100000      # створити й заповнити SQL (RestaurantsLab2) та MongoDB (restaurant.reviews)
dotnet run -- bench                   # бенчмарк, результати -> Lab2/results/
dotnet run -- mongo-demo              # базові запити MongoDB (C#)
dotnet run -- redis-demo              # базові запити Redis (C#)
bash Lab2/run-all.sh                  # повний цикл вимірювань (~20 хв)
```

Ті самі запити напряму в консолях БД:

```bash
docker exec -i restaurant-mongo mongosh restaurant < Database/mongo-queries.js
grep -v "^#" Database/redis-commands.txt | docker exec -i restaurant-redis redis-cli
```

`--sql localdb` перемикає SQL Server на `(localdb)\MSSQLLocalDB`. `dotnet run` без аргументів запускає демо ЛР1.
