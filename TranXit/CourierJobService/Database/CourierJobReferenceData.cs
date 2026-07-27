namespace CourierJobService.Database;

public static class CourierJobReferenceData
{
	public static JobStatus[] CreateJobStatuses() =>
	[
		new JobStatus { Id = 1, Status = "Open" },
		new JobStatus { Id = 2, Status = "Closed" },
		new JobStatus { Id = 3, Status = "Won" },
		new JobStatus { Id = 4, Status = "Lost" },
		new JobStatus { Id = 5, Status = "Bidding" },
		new JobStatus { Id = 6, Status = "InTransit" },
		new JobStatus { Id = 7, Status = "Delivered" }
	];

	public static CourierMode[] CreateCourierModes() =>
	[
		new CourierMode { Id = 1, Name = "Door to door" },
		new CourierMode { Id = 2, Name = "Port to port" },
		new CourierMode { Id = 3, Name = "Warehouse pickup" }
	];

	public static CargoMode[] CreateCargoModes() =>
	[
		new CargoMode { Id = 1, Name = "Sea freight" },
		new CargoMode { Id = 2, Name = "Air freight" },
		new CargoMode { Id = 3, Name = "Road freight" }
	];

	public static ItemType[] CreateItemTypes() =>
	[
		new ItemType { Id = 1, Name = "Cartons" },
		new ItemType { Id = 2, Name = "Pallets" },
		new ItemType { Id = 3, Name = "Machinery" },
		new ItemType { Id = 4, Name = "Documents" }
	];

	public static DeliveryType[] CreateDeliveryTypes() =>
	[
		new DeliveryType { Id = 1, Name = "Economy", NoOfDays = 22 },
		new DeliveryType { Id = 2, Name = "Standard", NoOfDays = 16 },
		new DeliveryType { Id = 3, Name = "Express", NoOfDays = 7 }
	];

	public static Country[] CreateCountries() =>
	[
		new Country { Id = 1, CountryName = "Pakistan" },
		new Country { Id = 2, CountryName = "Germany" },
		new Country { Id = 3, CountryName = "United Arab Emirates" }
	];

	public static City[] CreateCities() =>
	[
		new City { Id = 1, CityName = "Karachi", CountryId = 1 },
		new City { Id = 2, CityName = "Lahore", CountryId = 1 },
		new City { Id = 3, CityName = "Hamburg", CountryId = 2 },
		new City { Id = 4, CityName = "Berlin", CountryId = 2 },
		new City { Id = 5, CityName = "Dubai", CountryId = 3 }
	];
}
