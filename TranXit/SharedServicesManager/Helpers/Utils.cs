namespace SharedServicesManager.Helpers
{
	public interface IUtils
	{
		int Generate6DRandomCode();
	}
	public class Utils : IUtils
	{
		public int Generate6DRandomCode()
		{
			Random generator = new Random();
			string r = generator.Next(0, 1000000).ToString("D6");
			return Convert.ToInt32(r);
		}
	}
}
