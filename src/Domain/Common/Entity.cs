namespace Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    protected Entity() { }
    protected Entity(Guid id)
        => Id = id;

    public override bool Equals(object? obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (GetType() != obj.GetType())
        {
            return false;
        }

        var entity = (Entity)obj;

        return Id.Equals(entity.Id);
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity a, Entity b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Equals(b);
    }

    public static bool operator !=(Entity a, Entity b) => !(a == b);
}
