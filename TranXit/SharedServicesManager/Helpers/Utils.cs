using System.Security.Cryptography;

namespace SharedServicesManager.Helpers
{
	public interface IUtils
	{
		int Generate6DRandomCode();
		string GenerateJobNumber();
	}
	public class Utils : IUtils
	{
		public int Generate6DRandomCode()
		{
			Random generator = new Random();
			string randomNumber = generator.Next(100000, 1000000).ToString();
			if (randomNumber.Length == 5)
			{
				randomNumber += "0";
			}
			return Convert.ToInt32(randomNumber);
		}

		public string GenerateJobNumber()
		{
			return RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 8);
		}
	}
}
