using Microsoft.EntityFrameworkCore;

namespace AuthService.Database;

public partial class AuthDbContext : DbContext
{
	public AuthDbContext()
	{
	}

	public AuthDbContext(DbContextOptions<AuthDbContext> options)
		: base(options)
	{
	}

	public DbSet<Role> Roles { get; set; }

	public DbSet<User> Users { get; set; }


	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Role>(entity =>
		{
			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<User>(entity =>
		{
			entity.HasIndex(e => e.Email, "IX_Users").IsUnique();

			entity.Property(e => e.Email).HasMaxLength(256);
			entity.Property(e => e.Username).HasMaxLength(256);

			entity.HasOne(d => d.Role).WithMany(p => p.Users)
				.HasForeignKey(d => d.RoleId)
				.HasConstraintName("FK_Users_Roles");
		});

		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
