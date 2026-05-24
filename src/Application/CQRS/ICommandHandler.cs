namespace Application.CQRS;

/// <summary>
/// Handler interface for commands that mutate state.
/// Implement this interface to handle a specific command.
/// </summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
/// <typeparam name="TResult">The result type returned.</typeparam>
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}
