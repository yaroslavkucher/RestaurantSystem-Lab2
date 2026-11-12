GO
CREATE VIEW vw_ActiveMenuItems AS
SELECT Id, CategoryId, Name, Price, UpdatedAt, UpdatedBy 
FROM MenuItems 
WHERE IsDeleted = 0;
GO

CREATE VIEW vw_ActiveOrders AS
SELECT Id, RestaurantId, TableId, WaiterId, OrderDate, TotalAmount, Status 
FROM Orders 
WHERE IsDeleted = 0;
GO

CREATE VIEW vw_DailyOrderSummary AS
SELECT RestaurantId, CAST(OrderDate AS DATE) AS OrderDate, SUM(TotalAmount) AS TotalRevenue, COUNT(Id) AS OrdersCount
FROM Orders
WHERE IsDeleted = 0 AND Status = 'Оплачено'
GROUP BY RestaurantId, CAST(OrderDate AS DATE);
GO