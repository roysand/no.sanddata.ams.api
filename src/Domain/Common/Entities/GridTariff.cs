namespace Domain.Common.Entities;

/// <summary>A tariff of a grid company that locations can use. The rates live in <see cref="GridTariffRate"/>.</summary>
public class GridTariff : Entity
{
    public string Name { get; private set; } = null!;

    public GridTariff(Guid id, string name)
        : base(id)
    {
        Name = name;
        CreatedAt = UpdatedAt = DateTime.UtcNow;
    }

    public GridTariff() : base() { }
}
