namespace SharedServicesManager
{
    public sealed class Error(string error)
    {
        public IEnumerable<string> errors 
        { 
            get 
            { 
                return !String.IsNullOrEmpty(error) 
                    ? error.Split("\n").ToList()
                    : Enumerable.Empty<string>(); 
            } 
        }
    }
    public class Result<TValue>
    {
        public TValue? value { get; }
        public IEnumerable<string> error { get; }

        public bool isSuccess {  get; }

        private Result(TValue _value)
        {
            isSuccess = true;
            value = _value;
            error = Enumerable.Empty<string>();
        }

        private Result(Error _error)
        {
            isSuccess = false;
            value = default;
            error = _error.errors;
        }

        //happy path
        public static implicit operator Result<TValue>(TValue value) => new Result<TValue>(value);

        //error path
        public static implicit operator Result<TValue>(Error error) => new Result<TValue>(error);


    }
}
