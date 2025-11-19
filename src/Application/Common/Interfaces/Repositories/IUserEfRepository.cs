namespace Application.Common.Interfaces.Repositories;

public interface IUserEfRepository<T> : IEfRepository<T> where T : class
{
}
