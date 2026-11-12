CREATE NONCLUSTERED INDEX IX_Orders_Restaurant_Date 
ON Orders (RestaurantId, OrderDate)
INCLUDE (TotalAmount, Status);
GO

CREATE NONCLUSTERED INDEX IX_ActiveMenuItems_Category 
ON MenuItems (CategoryId, Name)
WHERE IsDeleted = 0;
GO