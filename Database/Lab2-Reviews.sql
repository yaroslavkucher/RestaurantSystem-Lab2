-- =====================================================================
-- ЛР2. Реляційний аналог MongoDB-колекції `reviews`.
-- Один документ відгуку в MongoDB  ->  6 таблиць у SQL.
-- Скрипт ідемпотентний: можна запускати повторно.
-- =====================================================================

IF OBJECT_ID('sp_InsertReviews') IS NOT NULL DROP PROCEDURE sp_InsertReviews;
IF TYPE_ID('ReviewTvp')       IS NOT NULL DROP TYPE ReviewTvp;
IF TYPE_ID('ReviewAspectTvp') IS NOT NULL DROP TYPE ReviewAspectTvp;
IF TYPE_ID('ReviewTagTvp')    IS NOT NULL DROP TYPE ReviewTagTvp;
IF TYPE_ID('ReviewDishTvp')   IS NOT NULL DROP TYPE ReviewDishTvp;
IF TYPE_ID('ReviewPhotoTvp')  IS NOT NULL DROP TYPE ReviewPhotoTvp;
IF TYPE_ID('ReviewReplyTvp')  IS NOT NULL DROP TYPE ReviewReplyTvp;
IF OBJECT_ID('ReviewReplies')       IS NOT NULL DROP TABLE ReviewReplies;
IF OBJECT_ID('ReviewPhotos')        IS NOT NULL DROP TABLE ReviewPhotos;
IF OBJECT_ID('ReviewDishRatings')   IS NOT NULL DROP TABLE ReviewDishRatings;
IF OBJECT_ID('ReviewTags')          IS NOT NULL DROP TABLE ReviewTags;
IF OBJECT_ID('ReviewAspectRatings') IS NOT NULL DROP TABLE ReviewAspectRatings;
IF OBJECT_ID('Reviews')             IS NOT NULL DROP TABLE Reviews;
IF OBJECT_ID('Tags')                IS NOT NULL DROP TABLE Tags;
GO

-- Сам відгук (корінь агрегату). Необов'язкові дані про візит — nullable-колонки.
CREATE TABLE Reviews (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    RestaurantId  INT NOT NULL REFERENCES Restaurants(Id),
    CustomerId    INT NOT NULL REFERENCES Customers(Id),
    OrderId       INT NULL,
    CreatedAt     DATETIME2(3) NOT NULL,
    Rating        INT NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    Comment       NVARCHAR(1000) NULL,
    VisitGuests   INT NULL,
    VisitOccasion NVARCHAR(50) NULL
);

-- Оцінки за аспектами (food / service / ambience / ...). Набір аспектів змінний.
CREATE TABLE ReviewAspectRatings (
    ReviewId INT NOT NULL REFERENCES Reviews(Id),
    Aspect   NVARCHAR(30) NOT NULL,
    Score    INT NOT NULL CHECK (Score BETWEEN 1 AND 5),
    PRIMARY KEY (ReviewId, Aspect)
);

-- Довідник тегів + зв'язок M:N
CREATE TABLE Tags (
    Id   INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE ReviewTags (
    ReviewId INT NOT NULL REFERENCES Reviews(Id),
    TagId    INT NOT NULL REFERENCES Tags(Id),
    PRIMARY KEY (ReviewId, TagId)
);

-- Оцінки конкретних страв у відгуку
CREATE TABLE ReviewDishRatings (
    ReviewId   INT NOT NULL REFERENCES Reviews(Id),
    MenuItemId INT NOT NULL REFERENCES MenuItems(Id),
    Rating     INT NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    Comment    NVARCHAR(300) NULL,
    PRIMARY KEY (ReviewId, MenuItemId)
);

-- Фото до відгуку
CREATE TABLE ReviewPhotos (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    ReviewId INT NOT NULL REFERENCES Reviews(Id),
    Url      NVARCHAR(300) NOT NULL,
    Caption  NVARCHAR(200) NULL
);

-- Відповідь адміністрації (0..1 на відгук)
CREATE TABLE ReviewReplies (
    ReviewId  INT PRIMARY KEY REFERENCES Reviews(Id),
    UserId    INT NOT NULL REFERENCES Users(Id),
    Text      NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL
);
GO

-- Індекси — еквівалентні індексам MongoDB (див. Lab2/Mongo/MongoReviewStore.cs)
CREATE INDEX IX_Reviews_Restaurant_Created ON Reviews (RestaurantId, CreatedAt DESC);
CREATE INDEX IX_ReviewTags_Tag             ON ReviewTags (TagId, ReviewId);
CREATE INDEX IX_ReviewDishRatings_MenuItem ON ReviewDishRatings (MenuItemId) INCLUDE (Rating);
CREATE INDEX IX_ReviewPhotos_Review        ON ReviewPhotos (ReviewId);
GO

-- =====================================================================
-- Вставка відгуків (1 або багато) однією процедурою з табличними параметрами.
-- Клієнт передає 6 TVP — по одному на кожну таблицю агрегату.
-- =====================================================================
CREATE TYPE ReviewTvp AS TABLE (
    Id INT PRIMARY KEY, RestaurantId INT NOT NULL, CustomerId INT NOT NULL, OrderId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL, Rating INT NOT NULL, Comment NVARCHAR(1000) NULL,
    VisitGuests INT NULL, VisitOccasion NVARCHAR(50) NULL);
CREATE TYPE ReviewAspectTvp AS TABLE (ReviewId INT NOT NULL, Aspect NVARCHAR(30) NOT NULL, Score INT NOT NULL);
CREATE TYPE ReviewTagTvp    AS TABLE (ReviewId INT NOT NULL, TagId INT NOT NULL);
CREATE TYPE ReviewDishTvp   AS TABLE (ReviewId INT NOT NULL, MenuItemId INT NOT NULL, Rating INT NOT NULL, Comment NVARCHAR(300) NULL);
CREATE TYPE ReviewPhotoTvp  AS TABLE (ReviewId INT NOT NULL, Url NVARCHAR(300) NOT NULL, Caption NVARCHAR(200) NULL);
CREATE TYPE ReviewReplyTvp  AS TABLE (ReviewId INT NOT NULL, UserId INT NOT NULL, Text NVARCHAR(1000) NOT NULL, CreatedAt DATETIME2(3) NOT NULL);
GO

CREATE PROCEDURE sp_InsertReviews
    @Reviews ReviewTvp READONLY,
    @Aspects ReviewAspectTvp READONLY,
    @Tags    ReviewTagTvp READONLY,
    @Dishes  ReviewDishTvp READONLY,
    @Photos  ReviewPhotoTvp READONLY,
    @Replies ReviewReplyTvp READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
        -- Id генерується на клієнті, щоб у SQL і MongoDB були однакові ідентифікатори
        SET IDENTITY_INSERT Reviews ON;
        INSERT INTO Reviews (Id, RestaurantId, CustomerId, OrderId, CreatedAt, Rating, Comment, VisitGuests, VisitOccasion)
        SELECT Id, RestaurantId, CustomerId, OrderId, CreatedAt, Rating, Comment, VisitGuests, VisitOccasion FROM @Reviews;
        SET IDENTITY_INSERT Reviews OFF;

        INSERT INTO ReviewAspectRatings (ReviewId, Aspect, Score)      SELECT ReviewId, Aspect, Score FROM @Aspects;
        INSERT INTO ReviewTags (ReviewId, TagId)                       SELECT ReviewId, TagId FROM @Tags;
        INSERT INTO ReviewDishRatings (ReviewId, MenuItemId, Rating, Comment) SELECT ReviewId, MenuItemId, Rating, Comment FROM @Dishes;
        INSERT INTO ReviewPhotos (ReviewId, Url, Caption)              SELECT ReviewId, Url, Caption FROM @Photos;
        INSERT INTO ReviewReplies (ReviewId, UserId, Text, CreatedAt)  SELECT ReviewId, UserId, Text, CreatedAt FROM @Replies;
    COMMIT;
END;
GO
