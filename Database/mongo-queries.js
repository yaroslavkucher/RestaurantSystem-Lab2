// =====================================================================
// ЛР2. Базові запити MongoDB до колекції reviews.
// Запуск:  docker exec -i restaurant-mongo mongosh restaurant < Database/mongo-queries.js
// (спершу заповніть дані: dotnet run -- seed --size 10000)
// =====================================================================

// ---------- CREATE
db.reviews.insertOne({
  _id: 900000001,
  restaurantId: 1,
  customerId: 42,
  createdAt: new Date(),
  rating: 5,
  comment: "Найкращий борщ у місті",
  aspects: { food: 5, service: 5, kidsRoom: 4 },          // новий аспект — без ALTER TABLE
  tags: ["смачно", "для-дітей"],
  dishes: [{ menuItemId: 1, name: "Борщ класичний", rating: 5 }],
  photos: [],
  visit: { guests: 3, occasion: "сімейна вечеря" }
});

// ---------- READ
db.reviews.findOne({ _id: 900000001 });

// останні 5 відгуків ресторану (індекс restaurant_createdAt)
db.reviews.find({ restaurantId: 1 }, { rating: 1, comment: 1, createdAt: 1 }).sort({ createdAt: -1 }).limit(5);

// відгуки з тегом + проєкція (індекс restaurant_tags_createdAt)
db.reviews.find({ restaurantId: 1, tags: "затишно" }, { _id: 1, rating: 1, tags: 1 }).sort({ createdAt: -1 }).limit(5);

// пошук по вкладеному масиву: страву #5 оцінили на 1
db.reviews.countDocuments({ dishes: { $elemMatch: { menuItemId: 5, rating: 1 } } });

// гнучка схема: поле є лише в частині документів
db.reviews.countDocuments({ "aspects.parking": { $lte: 2 } });
db.reviews.countDocuments({ reply: { $exists: false }, rating: { $lte: 2 } });   // негативні без відповіді

// ---------- UPDATE
db.reviews.updateOne({ _id: 900000001 },
  { $set: { reply: { userId: 1, text: "Дякуємо!", createdAt: new Date() } } });
db.reviews.updateOne({ _id: 900000001 },
  { $push: { photos: { url: "https://cdn.restaurant.local/demo.jpg", caption: "борщ" } } });
db.reviews.updateOne({ _id: 900000001 }, { $addToSet: { tags: "великі-порції" } });
db.reviews.updateOne({ _id: 900000001 }, { $inc: { "aspects.service": -1 } });
db.reviews.findOne({ _id: 900000001 }, { reply: 1, photos: 1, tags: 1, aspects: 1 });

// ---------- AGGREGATE
// рейтинг страв ресторану
db.reviews.aggregate([
  { $match: { restaurantId: 1 } },
  { $unwind: "$dishes" },
  { $group: { _id: "$dishes.menuItemId", name: { $first: "$dishes.name" },
              cnt: { $sum: 1 }, avg: { $avg: "$dishes.rating" } } },
  { $sort: { avg: -1 } },
  { $limit: 5 }
]);

// середні оцінки за аспектами (довільні ключі -> масив пар)
db.reviews.aggregate([
  { $match: { restaurantId: 1 } },
  { $project: { a: { $objectToArray: "$aspects" } } },
  { $unwind: "$a" },
  { $group: { _id: "$a.k", cnt: { $sum: 1 }, avg: { $avg: "$a.v" } } },
  { $sort: { _id: 1 } }
]);

// рейтинг ресторанів мережі
db.reviews.aggregate([
  { $group: { _id: "$restaurantId", cnt: { $sum: 1 }, avg: { $avg: "$rating" } } },
  { $sort: { avg: -1 } },
  { $limit: 5 }
]);

// ---------- INDEXES / EXPLAIN
db.reviews.getIndexes();
db.reviews.find({ restaurantId: 7, tags: "затишно" }).sort({ createdAt: -1 }).limit(20).explain("executionStats").executionStats.totalDocsExamined;

// ---------- DELETE
db.reviews.deleteOne({ _id: 900000001 });
db.reviews.countDocuments({ _id: 900000001 });
