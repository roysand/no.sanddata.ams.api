namespace Domain.Common;

public abstract class Entity(Guid id)
{
    private Guid Id { get; } = id;
    public  DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj == null)
            return false;

        if (GetType() != obj.GetType())
            return false;

        var entity = (Entity)obj;

        return Id.Equals(entity.Id);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public static bool operator ==(Entity a, Entity b)
    {
        if (ReferenceEquals(a, null) && ReferenceEquals(b, null))
            return true;

        if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
            return false;

        return a.Equals(b);
    }

    public static bool operator !=(Entity a, Entity b)
    {
        return !(a == b);
    }
}
