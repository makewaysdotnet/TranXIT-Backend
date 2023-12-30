namespace AccountService.Features.Authentication.Register
{
    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int? RoleId { get; set; }
    }
}
