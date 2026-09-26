namespace Application.Common.Interfaces.Repositories;

public interface IUserRepository<T> : IRepository<T> where T : class
{
}
