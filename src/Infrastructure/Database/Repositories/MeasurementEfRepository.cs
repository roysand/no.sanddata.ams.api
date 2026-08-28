using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class MeasurementEfRepository : GenericEfRepository<Measurement>, IMeasurementEfRepository<Measurement>
{
    public MeasurementEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
