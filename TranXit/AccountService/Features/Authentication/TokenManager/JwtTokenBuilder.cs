using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AccountService.Features.Authentication.TokenManager
{
    internal interface IJwtTokenBuilder
    {
        string BuildToken(TokenBuilderRequest requestModel);
    }
    internal class JwtTokenBuilder : IJwtTokenBuilder
    {
        public string BuildToken(TokenBuilderRequest requestModel)
        {
            var claims = new ClaimsIdentity(new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Email, requestModel.Email),
                new Claim(ClaimTypes.Role, requestModel.Role),
                new Claim(ClaimTypes.GivenName, requestModel.Username),
                new Claim("UserId", requestModel.UserId),
                new Claim("EmailVerified", requestModel.EmailVerified.ToString())
            });

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(requestModel.SecretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claims,
                Expires = DateTime.UtcNow.AddMinutes(requestModel.ExpiryMinutes),
                SigningCredentials = credentials
            };
            var securityTokenHandler = new JwtSecurityTokenHandler();
            var securityToken = securityTokenHandler.CreateToken(tokenDescriptor);
            return securityTokenHandler.WriteToken(securityToken);
        }

    }
}
