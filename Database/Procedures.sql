CREATE PROCEDURE sp_CreateOrder
    @RestaurantId INT,
    @TableId INT,
    @WaiterId INT,
    @CustomerId INT = NULL,
    @NewOrderId INT OUTPUT
AS
BEGIN
    INSERT INTO Orders (RestaurantId, TableId, WaiterId, CustomerId, OrderDate, Status)
    VALUES (@RestaurantId, @TableId, @WaiterId, @CustomerId, GETDATE(), 'Нове');
    
    SET @NewOrderId = SCOPE_IDENTITY();
END;
GO

CREATE PROCEDURE sp_AddOrderItem
    @OrderId INT,
    @MenuItemId INT,
    @Quantity INT
AS
BEGIN
    DECLARE @UnitPrice DECIMAL(10,2);
    -- Отримуємо ціну страви
    SELECT @UnitPrice = Price FROM MenuItems WHERE Id = @MenuItemId;

    INSERT INTO OrderItems (OrderId, MenuItemId, Quantity, UnitPrice)
    VALUES (@OrderId, @MenuItemId, @Quantity, @UnitPrice);

    -- Оновлюємо загальну суму замовлення за допомогою нашої функції
    UPDATE Orders
    SET TotalAmount = dbo.fn_CalculateOrderTotal(@OrderId)
    WHERE Id = @OrderId;
END;
GO

CREATE PROCEDURE sp_SoftDeleteUser
    @UserId INT
AS
BEGIN
    UPDATE Users SET IsDeleted = 1 WHERE Id = @UserId;
END;
GO

CREATE PROCEDURE sp_UpdateMenuItemPrice
    @MenuItemId INT,
    @NewPrice DECIMAL(10,2),
    @UpdatedByUserId INT
AS
BEGIN
    UPDATE MenuItems
    SET Price = @NewPrice,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedByUserId
    WHERE Id = @MenuItemId;
END;
GO