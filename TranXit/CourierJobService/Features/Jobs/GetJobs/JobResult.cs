namespace CourierJobService.Features.Jobs.GetJobs;

public class JobResult
{
	public int Id { get; init; } = default;
	public int CustomerId { get; init; } = default;
	public string? OriginCountry { get; init; }
	public string? DestinationCountry { get; init; }
	public string? JobNumber { get; init; }
	public double? MinBid { get; init; }
	public double? MaxBid { get; init; }
	public double? YourBid { get; init; }
	public DateTime? CreatedOnUtc { get; init; }
	public string? Status { get; init; }
	public int? StatusId { get; init; }
}
