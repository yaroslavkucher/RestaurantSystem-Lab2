using RestaurantSystem.Infrastructure;

// ЛР2: dotnet run -- seed | bench | mongo-demo | redis-demo
if (args.Length > 0)
    return await RestaurantSystem.Lab2.Lab2Cli.RunAsync(args);

string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=Restaurants;Trusted_Connection=True;TrustServerCertificate=True;";

try
{
    using var uow = new UnitOfWork(connectionString);
    
    Console.WriteLine("Отримуємо активне меню...");
    var menuItems = await uow.Menu.GetActiveMenuItemsAsync();
    foreach (var item in menuItems)
    {
        Console.WriteLine($"- {item.Name}: {item.Price} грн");
    }

    Console.WriteLine("\nОновлюємо ціну на страву (Id=1)...");
    await uow.Menu.UpdatePriceAsync(menuItemId: 1, newPrice: 150.50m, updatedByUserId: 2);

    Console.WriteLine("\nСтворення нового замовлення...");
    int newOrderId = await uow.Orders.CreateOrderAsync(
        restaurantId: 2, 
        tableId: 1, 
        waiterId: 2); 

    await uow.Orders.AddOrderItemAsync(newOrderId, menuItemId: 1, quantity: 2);
    
    uow.Commit();
    
    Console.WriteLine($"Всі операції успішні! Замовлення №{newOrderId} збережено, ціни оновлено.");
}
catch (Exception ex)
{
    Console.WriteLine($"Сталася помилка. Всі зміни відхилено. Деталі: {ex.Message}");
}
return 0;
