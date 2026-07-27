using CourierJobService.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourierJobService.Database;

public static class CourierJobDevelopmentSeeder
{
	public static async Task SeedAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<CourierJobDbContext>();

		await SeedSampleJobsAsync(db);
	}

	private static async Task SeedSampleJobsAsync(CourierJobDbContext db)
	{
		if (await db.Jobs.AnyAsync())
		{
			return;
		}

		var courierMode = await db.CourierModes.FirstAsync();
		var cargoMode = await db.CargoModes.FirstAsync();
		var itemType = await db.ItemTypes.FirstAsync();
		var deliveryType = await db.DeliveryTypes.FirstAsync();
		var originCountry = await db.Countries.SingleAsync(c => c.CountryName == "Pakistan");
		var destinationCountry = await db.Countries.SingleAsync(c => c.CountryName == "Germany");
		var originCity = await db.Cities.SingleAsync(c => c.CityName == "Karachi");
		var destinationCity = await db.Cities.SingleAsync(c => c.CityName == "Hamburg");

		var job = new Job
		{
			UserId = 1,
			CourierModeId = courierMode.Id,
			CargoModeId = cargoMode.Id,
			OriginCountryId = originCountry.Id,
			OriginCityId = originCity.Id,
			OriginAddress = "Warehouse 12, Port Qasim",
			DestinationCountryId = destinationCountry.Id,
			DestinationCityId = destinationCity.Id,
			DestinationAddress = "Hamburg port terminal",
			RecipientName = "M. Weber",
			RecipientEmail = "recipient@example.com",
			RecipientContact = "+49 40 000000",
			CreatedOnUtc = DateTime.UtcNow.AddDays(-1),
			PickupDateUtc = DateTime.UtcNow.AddDays(3),
			ExpiryDateUtc = DateTime.UtcNow.AddDays(6),
			JobNumber = "TX1001",
			JobStatusId = (int)JobStatusEnum.Bidding,
			JobItems =
			[
				new JobItem
				{
					ItemTypeId = itemType.Id,
					Name = "Textile cartons",
					Description = "Export cartons for retail shipment",
					Quantity = 24,
					Weight = 1180,
					DeclaredValue = 1750000,
					Dimensions = "40ft container"
				}
			],
			Biddings =
			[
				new Bidding
				{
					UserId = 2,
					TotalAmount = 2270000,
					IsInsurancePolicy = true,
					PickupCharges = 185000,
					HandlingCharges = 240000,
					CustomClearanceCharges = 95000,
					JobStatusId = (int)JobStatusEnum.Open,
					BiddingProposals =
					[
						new BiddingProposal
						{
							DeliveryTypeId = deliveryType.Id,
							IsBaseBid = true,
							DeliveryDateUtc = DateTime.UtcNow.AddDays(18),
							Total = 2270000
						}
					],
					BiddingCharges =
					[
						new BiddingCharge { Name = "Ocean freight", Description = "Karachi to Hamburg", Amount = 1640000 },
						new BiddingCharge { Name = "Origin handling", Description = "Terminal and documentation", Amount = 185000 },
						new BiddingCharge { Name = "Customs clearance", Description = "Export and import filings", Amount = 95000 }
					]
				}
			]
		};

		db.Jobs.Add(job);
		await db.SaveChangesAsync();
	}

}
