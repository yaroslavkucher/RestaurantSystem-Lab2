CREATE TABLE Restaurants (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Address NVARCHAR(255),
    Phone NVARCHAR(20)
);

CREATE TABLE Roles (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL
);

CREATE TABLE Customers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Phone NVARCHAR(20),
    Email NVARCHAR(100),
    IsDeleted BIT DEFAULT 0
);

CREATE TABLE MenuCategories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL,
    Description NVARCHAR(255)
);

CREATE TABLE Suppliers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    ContactPerson NVARCHAR(100),
    Phone NVARCHAR(20),
    IsDeleted BIT DEFAULT 0
);

CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    RestaurantId INT REFERENCES Restaurants(Id),
    RoleId INT REFERENCES Roles(Id),
    Username NVARCHAR(50) NOT NULL,
    PasswordHash NVARCHAR(255) NOT NULL,
    FullName NVARCHAR(100),
    IsDeleted BIT DEFAULT 0
);

CREATE TABLE Tables (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    RestaurantId INT REFERENCES Restaurants(Id),
    TableNumber INT NOT NULL,
    Capacity INT NOT NULL
);

CREATE TABLE Reservations (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT REFERENCES Customers(Id),
    TableId INT REFERENCES Tables(Id),
    ReservationDate DATETIME NOT NULL,
    NumberOfGuests INT NOT NULL,
    Status NVARCHAR(20) DEFAULT 'Очікується',
    IsDeleted BIT DEFAULT 0
);

CREATE TABLE Ingredients (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    SupplierId INT REFERENCES Suppliers(Id),
    Name NVARCHAR(100) NOT NULL,
    UnitOfMeasurement NVARCHAR(20)
);

CREATE TABLE MenuItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CategoryId INT REFERENCES MenuCategories(Id),
    Name NVARCHAR(100) NOT NULL,
    Price DECIMAL(10,2) NOT NULL,
    IsDeleted BIT DEFAULT 0,
    UpdatedAt DATETIME,
    UpdatedBy INT REFERENCES Users(Id)
);

CREATE TABLE MenuItemIngredients (
    MenuItemId INT REFERENCES MenuItems(Id),
    IngredientId INT REFERENCES Ingredients(Id),
    Quantity DECIMAL(10,3) NOT NULL,
    PRIMARY KEY (MenuItemId, IngredientId)
);

CREATE TABLE Inventory (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    RestaurantId INT REFERENCES Restaurants(Id),
    IngredientId INT REFERENCES Ingredients(Id),
    QuantityInStock DECIMAL(10,3) NOT NULL DEFAULT 0,
    UpdatedAt DATETIME,
    UpdatedBy INT REFERENCES Users(Id)
);

CREATE TABLE Orders (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    RestaurantId INT REFERENCES Restaurants(Id),
    TableId INT REFERENCES Tables(Id),
    WaiterId INT REFERENCES Users(Id),
    CustomerId INT NULL REFERENCES Customers(Id),
    OrderDate DATETIME DEFAULT GETDATE(),
    TotalAmount DECIMAL(10,2) DEFAULT 0,
    Status NVARCHAR(20) DEFAULT 'Нове',
    IsDeleted BIT DEFAULT 0
);

CREATE TABLE OrderItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT REFERENCES Orders(Id),
    MenuItemId INT REFERENCES MenuItems(Id),
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL
);

CREATE TABLE Payments (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT REFERENCES Orders(Id),
    PaymentDate DATETIME DEFAULT GETDATE(),
    Amount DECIMAL(10,2) NOT NULL,
    PaymentMethod NVARCHAR(50) NOT NULL
);