CREATE TRIGGER trg_Inventory_UpdateAudit
ON Inventory
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
        
    IF NOT UPDATE(UpdatedAt)
    BEGIN
        UPDATE Inventory
        SET UpdatedAt = GETDATE()
        FROM Inventory i
        INNER JOIN inserted ins ON i.Id = ins.Id;
    END
END;
GO

CREATE TRIGGER trg_MenuItems_UpdateAudit
ON MenuItems
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    
    IF NOT UPDATE(UpdatedAt)
    BEGIN
        UPDATE MenuItems
        SET UpdatedAt = GETDATE()
        FROM MenuItems m
        INNER JOIN inserted i ON m.Id = i.Id;
    END
END;
GO

CREATE TRIGGER trg_Orders_SoftDelete
ON Orders
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Orders
    SET IsDeleted = 1
    WHERE Id IN (SELECT Id FROM deleted);
END;
GO

CREATE TRIGGER trg_Users_SoftDelete
ON Users
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Users SET IsDeleted = 1 WHERE Id IN (SELECT Id FROM deleted);
END;
GO

CREATE TRIGGER trg_Customers_SoftDelete
ON Customers
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Customers SET IsDeleted = 1 WHERE Id IN (SELECT Id FROM deleted);
END;
GO

CREATE TRIGGER trg_Reservations_SoftDelete
ON Reservations
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Reservations SET IsDeleted = 1 WHERE Id IN (SELECT Id FROM deleted);
END;
GO

CREATE TRIGGER trg_MenuItems_SoftDelete
ON MenuItems
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE MenuItems SET IsDeleted = 1 WHERE Id IN (SELECT Id FROM deleted);
END;
GO

CREATE TRIGGER trg_Suppliers_SoftDelete
ON Suppliers
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Suppliers SET IsDeleted = 1 WHERE Id IN (SELECT Id FROM deleted);
END;
GO