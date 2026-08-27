using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CourierJobService.Features.Bids.Shared;

public static class QuoteAmount
{
	// Keep cents within 15 decimal digits for the existing SQL double and JS number boundary.
	public const decimal Maximum = 9_999_999_999_999.99m;
	public const string ValidationMessage = "Amounts must be nonnegative, at most 9999999999999.99, and have no fractional cents";

	public static bool IsValid(decimal value)
		=> value >= 0 && value <= Maximum && decimal.Truncate(value * 100) == value * 100;

	public static bool TrySum(IEnumerable<decimal> values, out decimal total)
	{
		total = 0;
		foreach (var value in values)
		{
			if (!IsValid(value) || value > Maximum - total)
			{
				return false;
			}
			total += value;
		}
		return true;
	}

	public static bool IsValidStored(double? value)
		=> value.HasValue && double.IsFinite(value.Value) &&
			value.Value >= 0 && value.Value <= (double)Maximum && IsValid((decimal)value.Value) &&
			(double)(decimal)value.Value == value.Value;
}

public sealed class QuoteAmountJsonConverter : JsonConverter<decimal>
{
	public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.Number)
		{
			throw new JsonException("Quote amounts must be JSON numbers");
		}
		ReadOnlySpan<byte> number = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
		var decimalPoint = number.IndexOf((byte)'.');
		// Check the original number before decimal parsing can round excess precision.
		if (number.Contains((byte)'e') || number.Contains((byte)'E') ||
			(decimalPoint >= 0 && number.Length - decimalPoint - 1 > 2) ||
			!reader.TryGetDecimal(out var value))
		{
			throw new JsonException("Quote amounts must use plain decimal notation with at most two decimal places");
		}
		return value;
	}

	public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
		=> writer.WriteNumberValue(value);
}
