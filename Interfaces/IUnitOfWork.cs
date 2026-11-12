namespace RestaurantSystem.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IOrderRepository Orders { get; }
    IMenuRepository Menu { get; }
    void Commit();
    void Rollback();
}