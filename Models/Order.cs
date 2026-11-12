namespace RestaurantSystem.Models;

public class Order
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int TableId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}