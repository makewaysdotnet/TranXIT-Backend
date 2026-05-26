namespace AccountService.Features.Authentication.AccountVerification;

internal static class VerificationCodeHasher
{
	public static string Format(int code) => code.ToString("D6");

	public static string Hash(string code) => BC.EnhancedHashPassword(code);

	public static bool Verify(string code, string? hash)
	{
		return !string.IsNullOrWhiteSpace(hash) &&
			BC.EnhancedVerify(code, hash);
	}
}
