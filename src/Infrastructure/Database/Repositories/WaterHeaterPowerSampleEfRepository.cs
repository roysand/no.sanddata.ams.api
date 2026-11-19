using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Infrastructure.Database.Repositories;

public class WaterHeaterPowerSampleEfRepository
    : GenericEfRepository<WaterHeaterPowerSample>, IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>
{
    public WaterHeaterPowerSampleEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }
}
