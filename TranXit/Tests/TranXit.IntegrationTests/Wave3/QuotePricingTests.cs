extern alias CourierJobService;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;
using JobItem = CourierJobService::CourierJobService.Database.JobItem;
using QuoteAmount = CourierJobService::CourierJobService.Features.Bids.Shared.QuoteAmount;

namespace TranXit.IntegrationTests.Wave3;

public sealed class QuotePricingTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Theory(DisplayName = "T-COUR-4.QuoteAllInRoundTrip")]
	[InlineData(false)]
	[InlineData(true)]
	public async Task QuoteAllInRoundTrip(bool selectAlternative)
	{
		// UC-COUR-4, UC-CUST-4, UC-CUST-5
		int secondItemId;
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			(await db.JobItems.FindAsync(1))!.Quantity = 3;
			var secondItem = new JobItem { JobId = 1, Name = "Second quoted item", Quantity = 2, ItemTypeId = 1 };
			db.JobItems.Add(secondItem);
			await db.SaveChangesAsync();
			secondItemId = secondItem.Id;
		}
		var payload = ValidPayload();
		payload["bidCustomCharges"]!.AsArray().Add(new JsonObject { ["name"] = "Crating", ["amount"] = 5.05m });
		var baseProposal = Proposal(payload);
		baseProposal["total"] = 85.85m;
		baseProposal["bidProposalItems"] = new JsonArray(Line(1, 10.10m, 30.30m), Line(secondItemId, 20.20m, 40.40m));
		var alternative = baseProposal.DeepClone().AsObject();
		alternative["isBaseBid"] = false;
		alternative["total"] = 92.92m;
		alternative["bidProposalItems"] = new JsonArray(Line(1, 11.11m, 33.33m), Line(secondItemId, 22.22m, 44.44m));
		payload["bidProposals"]!.AsArray().Add(alternative);

		var bidId = await CreateAsync(payload);
		var listing = await ReadCourierJobAsync();
		listing.GetProperty("yourBid").GetDecimal().Should().Be(85.85m);
		listing.GetProperty("minBid").GetDecimal().Should().Be(85.85m);
		listing.GetProperty("maxBid").GetDecimal().Should().Be(85.85m);
		await using var readDb = Fixture.CreateCourierJobDbContext();
		var bid = await readDb.Biddings.AsNoTracking().Include(bid => bid.BiddingCharges)
			.Include(bid => bid.BiddingProposals).ThenInclude(proposal => proposal.BiddingProposalItems)
			.SingleAsync(bid => bid.Id == bidId);
		((decimal)bid.TotalAmount).Should().Be(85.85m);
		bid.BiddingCharges.Should().OnlyContain(charge => charge.Amount.HasValue);
		bid.BiddingCharges.Select(charge => (decimal)charge.Amount!.Value).Should().BeEquivalentTo([4.04m, 5.05m]);
		bid.BiddingProposals.Select(proposal => (decimal)proposal.Total!.Value).Should().BeEquivalentTo([85.85m, 92.92m]);
		bid.BiddingProposals.SelectMany(proposal => proposal.BiddingProposalItems)
			.Select(item => (decimal)item.ItemTotal!.Value).Should().BeEquivalentTo([30.30m, 40.40m, 33.33m, 44.44m]);
		var selected = bid.BiddingProposals.Single(proposal => proposal.IsBaseBid == !selectAlternative);
		var selectedPrice = selectAlternative ? 92.92m : 85.85m;
		var historyBefore = await ReadHistoryAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var award = await CourierClient.PutAsJsonAsync("/api/bids/status", new { bidId, bidProposalId = selected.Id, status = 3 });
		award.StatusCode.Should().Be(HttpStatusCode.OK, await award.Content.ReadAsStringAsync());
		(await award.ReadApiResultAsync<BidValue>()).IsSuccess.Should().BeTrue();
		(await ReadHistoryAsync()).Should().Be(historyBefore, "acceptance must not rewrite any quoted proposal or charge");
		await using var afterDb = Fixture.CreateCourierJobDbContext();
		((decimal)(await afterDb.Biddings.FindAsync(bidId))!.TotalAmount).Should().Be(selectedPrice);
		(await afterDb.Jobs.FindAsync(1))!.AcceptedBidProposalId.Should().Be(selected.Id);
		listing = await ReadCourierJobAsync();
		listing.GetProperty("yourBid").GetDecimal().Should().Be(selectedPrice);
	}

	[Theory(DisplayName = "T-COUR-4.QuoteZeroAndMaximumRoundTrip")]
	[InlineData(false)]
	[InlineData(true)]
	public async Task QuoteZeroAndMaximumRoundTrip(bool maximum)
	{
		// UC-COUR-4, UC-CUST-4, UC-CUST-5
		var before = await ReadCourierJobAsync();
		before.GetProperty("yourBid").ValueKind.Should().Be(JsonValueKind.Null);
		before.GetProperty("minBid").ValueKind.Should().Be(JsonValueKind.Null);
		var amount = maximum ? QuoteAmount.Maximum : 0m;
		var payload = ValidPayload();
		payload["pickupCharges"] = amount;
		payload["handlingCharges"] = 0;
		payload["customClearanceCharges"] = 0;
		payload["bidCustomCharges"] = new JsonArray();
		Proposal(payload)["bidProposalItems"] = new JsonArray();
		Proposal(payload)["total"] = amount;
		var bidId = await CreateAsync(payload);
		var job = await ReadCourierJobAsync();
		job.GetProperty("yourBid").GetDecimal().Should().Be(amount);
		job.GetProperty("minBid").GetDecimal().Should().Be(amount);
		job.GetProperty("maxBid").GetDecimal().Should().Be(amount);
		await using var db = Fixture.CreateCourierJobDbContext();
		var selected = await db.BiddingProposals.SingleAsync(proposal => proposal.BiddingId == bidId);
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var accepted = await CourierClient.PutAsJsonAsync("/api/bids/status", new { bidId, bidProposalId = selected.Id, status = 3 });
		accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
		(await ReadCourierJobAsync()).GetProperty("yourBid").GetDecimal().Should().Be(amount);
	}

	public static IEnumerable<object[]> InvalidAmounts()
	{
		foreach (var field in new[] { "pickupCharges", "handlingCharges", "customClearanceCharges", "custom", "total", "unitPrice", "itemTotal" })
		{
			foreach (var amount in new[] { -0.01m, 0.001m, 10_000_000_000_000m })
			{
				yield return [field, amount];
			}
		}
	}

	[Theory(DisplayName = "T-COUR-4.QuoteInvalidAmountNoWrites")]
	[MemberData(nameof(InvalidAmounts))]
	public async Task QuoteInvalidAmountNoWrites(string field, decimal amount)
	{
		// UC-COUR-4
		var payload = ValidPayload();
		SetAmount(payload, field, JsonValue.Create(amount));
		await RejectWithoutWritesAsync(payload);
	}

	[Theory(DisplayName = "T-COUR-4.QuoteMalformedNumberNoWrites")]
	[InlineData("\"abc\"")]
	[InlineData("\"12.34\"")]
	[InlineData("\"NaN\"")]
	[InlineData("\"Infinity\"")]
	[InlineData("1e2")]
	[InlineData("1.230")]
	[InlineData("1.230000000000000000000000000001")]
	[InlineData("0.000000000000000000000000000001")]
	[InlineData("1e1000")]
	[InlineData("null")]
	public async Task QuoteMalformedNumberNoWrites(string rawNumber)
	{
		// UC-COUR-4
		var payload = ValidPayload();
		payload["pickupCharges"] = JsonNode.Parse(rawNumber);
		await RejectWithoutWritesAsync(payload);
	}

	[Theory(DisplayName = "T-COUR-4.QuoteInvalidCompositionNoWrites")]
	[InlineData("total")]
	[InlineData("unitExtension")]
	[InlineData("duplicateBase")]
	[InlineData("noBase")]
	[InlineData("duplicateItem")]
	[InlineData("sumOverflow")]
	[InlineData("nullProposals")]
	[InlineData("nullProposal")]
	[InlineData("nullItems")]
	[InlineData("nullItem")]
	[InlineData("nullCharges")]
	[InlineData("nullCharge")]
	public async Task QuoteInvalidCompositionNoWrites(string problem)
	{
		// UC-COUR-4
		var payload = ValidPayload();
		var proposal = Proposal(payload);
		switch (problem)
		{
			case "total": proposal["total"] = 36.75m; break;
			case "unitExtension": SetAmount(payload, "unitPrice", JsonValue.Create(1.12m)); break;
			case "duplicateBase": payload["bidProposals"]!.AsArray().Add(proposal.DeepClone()); break;
			case "noBase": proposal["isBaseBid"] = false; break;
			case "duplicateItem":
				proposal["bidProposalItems"]!.AsArray().Add(proposal["bidProposalItems"]![0]!.DeepClone());
				proposal["total"] = 63.38m;
				break;
			case "sumOverflow": payload["pickupCharges"] = QuoteAmount.Maximum; proposal["total"] = QuoteAmount.Maximum; break;
			case "nullProposals": payload["bidProposals"] = null; break;
			case "nullProposal": payload["bidProposals"]!.AsArray().Add((JsonNode?)null); break;
			case "nullItems": proposal["bidProposalItems"] = null; break;
			case "nullItem": proposal["bidProposalItems"]!.AsArray().Add((JsonNode?)null); break;
			case "nullCharges": payload["bidCustomCharges"] = null; break;
			case "nullCharge": payload["bidCustomCharges"]!.AsArray().Add((JsonNode?)null); break;
			default: throw new ArgumentOutOfRangeException(nameof(problem));
		}
		await RejectWithoutWritesAsync(payload);
	}

	[Fact(DisplayName = "T-CUST-5.LegacyQuoteMismatchNotRepriced")]
	public async Task LegacyQuoteMismatchNotRepriced()
	{
		// UC-CUST-5
		var bidId = await CreateAsync(ValidPayload());
		await using var db = Fixture.CreateCourierJobDbContext();
		var bid = await db.Biddings.Include(bid => bid.BiddingProposals).SingleAsync(bid => bid.Id == bidId);
		bid.TotalAmount = 73.48;
		await db.SaveChangesAsync();
		var history = await ReadHistoryAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var response = await CourierClient.PutAsJsonAsync("/api/bids/status", new
		{
			bidId, bidProposalId = bid.BiddingProposals.Single().Id, status = 3
		});
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.ReadApiResultAsync<BidValue>()).Error.Should().Contain(error => error.Contains("require review"));
		(await ReadHistoryAsync()).Should().Be(history);
		await using var afterDb = Fixture.CreateCourierJobDbContext();
		var job = await afterDb.Jobs.FindAsync(1);
		job!.AcceptedBidProposalId.Should().BeNull();
		job.IsJobStatusFromBid.Should().BeFalse();
		((decimal)(await afterDb.Biddings.FindAsync(bidId))!.TotalAmount).Should().Be(73.48m);
	}

	[Fact(DisplayName = "T-COUR-4.QuoteNumericBoundaryRoundTrips")]
	public void QuoteNumericBoundaryRoundTrips()
	{
		// UC-COUR-4
		var values = new[] { 0m, 0.01m, 0.1m, 0.29m, 1.01m, 85.85m, 999_999.99m,
			9_999_999_999.99m, 999_999_999_999.99m, QuoteAmount.Maximum };
		foreach (var value in values)
		{
			QuoteAmount.IsValid(value).Should().BeTrue();
			QuoteAmount.IsValidStored((double)value).Should().BeTrue();
			((decimal)(double)value).Should().Be(value);
			JsonSerializer.Deserialize<decimal>(JsonSerializer.Serialize((double)value)).Should().Be(value);
		}
		QuoteAmount.TrySum([0.1m, 0.2m], out var total).Should().BeTrue();
		total.Should().Be(0.30m);
		QuoteAmount.TrySum([QuoteAmount.Maximum, 0.01m], out _).Should().BeFalse();
		QuoteAmount.IsValidStored(double.NaN).Should().BeFalse();
		QuoteAmount.IsValidStored(double.PositiveInfinity).Should().BeFalse();
		QuoteAmount.IsValidStored(1.2300000000000002).Should().BeFalse();
	}

	private static JsonObject ValidPayload() => new()
	{
		["jobId"] = 1, ["pickupCharges"] = 1.01m, ["handlingCharges"] = 2.02m,
		["customClearanceCharges"] = 3.03m, ["isInsurancePolicy"] = true,
		["bidCustomCharges"] = new JsonArray(new JsonObject { ["name"] = "Freight", ["amount"] = 4.04m }),
		["bidProposals"] = new JsonArray(new JsonObject
		{
			["deliveryTypeId"] = 1, ["isBaseBid"] = true, ["deliveryDate"] = DateTime.UtcNow.AddDays(5),
			["total"] = 36.74m, ["bidProposalItems"] = new JsonArray(Line(1, 1.11m, 26.64m))
		})
	};

	private static JsonObject Line(int jobItemId, decimal unitPrice, decimal itemTotal) => new()
	{
		["jobItemId"] = jobItemId, ["unitPrice"] = unitPrice, ["itemTotal"] = itemTotal
	};

	private static JsonObject Proposal(JsonObject payload) => payload["bidProposals"]![0]!.AsObject();

	private static void SetAmount(JsonObject payload, string field, JsonNode? value)
	{
		var owner = field switch
		{
			"custom" => payload["bidCustomCharges"]![0]!.AsObject(),
			"total" => Proposal(payload),
			"unitPrice" or "itemTotal" => Proposal(payload)["bidProposalItems"]![0]!.AsObject(),
			_ => payload
		};
		owner[field == "custom" ? "amount" : field] = value;
	}

	private Task<HttpResponseMessage> PostAsync(JsonObject payload)
	{
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));
		return CourierClient.PostAsync("/api/bids", new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
	}

	private async Task<int> CreateAsync(JsonObject payload)
	{
		using var response = await PostAsync(payload);
		response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeTrue();
		return result.Value!.BidId;
	}

	private async Task<JsonElement> ReadCourierJobAsync()
	{
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));
		using var response = await CourierClient.GetAsync("/api/jobs?page=1&pageSize=10");
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
		var result = await response.ReadApiResultAsync<JsonElement>();
		result.IsSuccess.Should().BeTrue();
		return result.Value.GetProperty("items").EnumerateArray().Single(job => job.GetProperty("id").GetInt32() == 1);
	}

	private async Task RejectWithoutWritesAsync(JsonObject payload)
	{
		using var response = await PostAsync(payload);
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
		await using var db = Fixture.CreateCourierJobDbContext();
		(await db.Biddings.CountAsync()).Should().Be(0);
		(await db.BiddingProposals.CountAsync()).Should().Be(0);
		(await db.BiddingProposalItems.CountAsync()).Should().Be(0);
		(await db.BiddingCharges.CountAsync()).Should().Be(0);
		var job = await db.Jobs.FindAsync(1);
		job!.JobStatusId.Should().Be(5);
		job.IsJobStatusFromBid.Should().BeFalse();
		job.AcceptedBidProposalId.Should().BeNull();
	}

	private async Task<string> ReadHistoryAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		return JsonSerializer.Serialize(new
		{
			Proposals = await db.BiddingProposals.OrderBy(proposal => proposal.Id)
				.Select(proposal => new { proposal.Id, proposal.Total, proposal.IsBaseBid, proposal.DeliveryDateUtc }).ToListAsync(),
			Items = await db.BiddingProposalItems.OrderBy(item => item.Id)
				.Select(item => new { item.Id, item.UnitPrice, item.ItemTotal, item.JobItemId }).ToListAsync(),
			Charges = await db.BiddingCharges.OrderBy(charge => charge.Id)
				.Select(charge => new { charge.Id, charge.Name, charge.Amount }).ToListAsync()
		});
	}
}
