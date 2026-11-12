using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantSystem.Interfaces;
using RestaurantSystem.Repositories;

namespace RestaurantSystem.Infrastructure;

public class UnitOfWork : IUnitOfWork
{
    private readonly IDbConnection _connection;
    private IDbTransaction _transaction;
    private IOrderRepository? _orderRepository;
    private IMenuRepository? _menuRepository;

    public UnitOfWork(string connectionString)
    {
        _connection = new SqlConnection(connectionString);
        _connection.Open();
        _transaction = _connection.BeginTransaction();
    }

    public IMenuRepository Menu => _menuRepository ??= new MenuRepository(_transaction);
    public IOrderRepository Orders => _orderRepository ??= new OrderRepository(_transaction);

    public void Commit()
    {
        try
        {
            _transaction.Commit();
        }
        catch
        {
            _transaction.Rollback();
            throw;
        }
        finally
        {
            _transaction.Dispose();
            _transaction = _connection.BeginTransaction(); 
        }
    }

    public void Rollback()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = _connection.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection?.Dispose();
    }
}