namespace Domain.Common.Entities;

public class ExchangeRate : Entity
{
    public string CurrencyPair { get; private set; } = null!;
    public DateOnly RateDate { get; private set; }
    public decimal Rate { get; private set; }

    public ExchangeRate(Guid id, string currencyPair, DateOnly rateDate, decimal rate)
        : base(id)
    {
        CurrencyPair = currencyPair;
        RateDate = rateDate;
        Rate = rate;
    }

    public ExchangeRate() : base() { }
}
