CREATE FUNCTION fn_CalculateOrderTotal (@OrderId INT)
RETURNS DECIMAL(10,2)
AS
BEGIN
    DECLARE @Total DECIMAL(10,2);
    SELECT @Total = SUM(Quantity * UnitPrice)
    FROM OrderItems
    WHERE OrderId = @OrderId;
    
    RETURN ISNULL(@Total, 0);
END;
GO

CREATE FUNCTION fn_GetAvailableTables (@RestaurantId INT, @Date DATE)
RETURNS TABLE
AS
RETURN (
    SELECT t.Id, t.TableNumber, t.Capacity
    FROM Tables t
    WHERE t.RestaurantId = @RestaurantId
      AND t.Id NOT IN (
          SELECT r.TableId 
          FROM Reservations r 
          WHERE r.IsDeleted = 0 
            AND CAST(r.ReservationDate AS DATE) = @Date 
            AND r.Status != 'Скасовано'
      )
);
GO