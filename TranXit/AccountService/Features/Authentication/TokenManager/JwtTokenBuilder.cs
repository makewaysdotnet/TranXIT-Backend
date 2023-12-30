using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AccountService.Features.Authentication.TokenManager
{
    internal interface IJwtTokenBuilder
    {
        string BuildToken(TokenBuilderRequestModel requestModel);
    }
    internal class JwtTokenBuilder : IJwtTokenBuilder
    {
        public string BuildToken(TokenBuilderRequestModel requestModel)
        {
            var claims = new ClaimsIdentity(new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Email, requestModel.Email),
                new Claim(ClaimTypes.Role, requestModel.Role),
                new Claim(ClaimTypes.GivenName, requestModel.Username),
                new Claim("UserId", requestModel.UserId)
            });

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(requestModel.SecretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claims,
                Expires = DateTime.Now.AddMinutes(requestModel.ExpiryMinutes),
                SigningCredentials = credentials
            };
            var securityTokenHandler = new JwtSecurityTokenHandler();
            var securityToken = securityTokenHandler.CreateToken(tokenDescriptor);
            return securityTokenHandler.WriteToken(securityToken);
        }

    }
}
