using Microsoft.EntityFrameworkCore;

namespace AccountService.Database;

public partial class AccountDbContext : DbContext
{
	public AccountDbContext()
	{
	}

	public AccountDbContext(DbContextOptions<AccountDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Role> Roles { get; set; }

	public virtual DbSet<User> Users { get; set; }

	public virtual DbSet<UserFile> UserFiles { get; set; }

	public virtual DbSet<UserImage> UserImages { get; set; }

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
			entity.Property(e => e.CodeSentAtUtc).HasColumnType("datetime");
			entity.Property(e => e.Email).HasMaxLength(256);
			entity.Property(e => e.Phone).HasMaxLength(50);
			entity.Property(e => e.Provider).HasMaxLength(100);
			entity.Property(e => e.Username).HasMaxLength(256);

			entity.HasOne(d => d.Role).WithMany(p => p.Users)
				.HasForeignKey(d => d.RoleId)
				.HasConstraintName("FK_Users_Roles");
		});

		modelBuilder.Entity<UserFile>(entity =>
		{
			entity.Property(e => e.Name).HasMaxLength(50);
			entity.Property(e => e.Type).HasMaxLength(50);

			entity.HasOne(d => d.User).WithMany(p => p.UserFiles)
				.HasForeignKey(d => d.UserId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("FK_UserFiles_Users");
		});

		modelBuilder.Entity<UserImage>(entity =>
		{
			entity.HasNoKey();

			entity.HasIndex(e => e.UserId, "UQ__UserImag__1788CC4D528CE71E").IsUnique();

			entity.Property(e => e.Id).ValueGeneratedOnAdd();
			entity.Property(e => e.Name).HasMaxLength(50);
			entity.Property(e => e.Type).HasMaxLength(50);

			entity.HasOne(d => d.User).WithOne()
				.HasForeignKey<UserImage>(d => d.UserId)
				.HasConstraintName("FK_UserImages_User");
		});

		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
