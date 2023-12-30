using System;
using System.Collections.Generic;

namespace AccountService.Database;

public partial class User
{
    public int Id { get; set; }

    public string Email { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public string Username { get; set; } = null!;

    public int? RoleId { get; set; }

    public string? Provider { get; set; }

    public bool? IsEmailVerified { get; set; }

    public virtual Role? Role { get; set; }
}
