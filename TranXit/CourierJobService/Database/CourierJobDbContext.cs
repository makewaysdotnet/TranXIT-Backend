using Microsoft.EntityFrameworkCore;

namespace CourierJobService.Database;

public partial class CourierJobDbContext : DbContext
{
	public CourierJobDbContext()
	{
	}

	public CourierJobDbContext(DbContextOptions<CourierJobDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Bidding> Biddings { get; set; }

	public virtual DbSet<BiddingCharge> BiddingCharges { get; set; }

	public virtual DbSet<BiddingProposal> BiddingProposals { get; set; }

	public virtual DbSet<BiddingProposalItem> BiddingProposalItems { get; set; }

	public virtual DbSet<CargoMode> CargoModes { get; set; }

	public virtual DbSet<City> Cities { get; set; }

	public virtual DbSet<Country> Countries { get; set; }

	public virtual DbSet<CourierMode> CourierModes { get; set; }

	public virtual DbSet<DeliveryType> DeliveryTypes { get; set; }

	public virtual DbSet<ItemType> ItemTypes { get; set; }

	public virtual DbSet<Job> Jobs { get; set; }

	public virtual DbSet<JobItem> JobItems { get; set; }

	public virtual DbSet<JobStatus> JobStatuses { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Bidding>(entity =>
		{
			entity.HasIndex(e => new { e.UserId, e.JobId }, "UK_Biddings").IsUnique();

			entity.HasOne(d => d.Job).WithMany(p => p.Biddings)
				.HasForeignKey(d => d.JobId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("FK_Biddings_Jobs");
		});

		modelBuilder.Entity<BiddingCharge>(entity =>
		{
			entity.Property(e => e.Description).HasMaxLength(100);
			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);

			entity.HasOne(d => d.Bidding).WithMany(p => p.BiddingCharges)
				.HasForeignKey(d => d.BiddingId)
				.HasConstraintName("FK_BiddingCharges_Biddings");
		});

		modelBuilder.Entity<BiddingProposal>(entity =>
		{
			entity.Property(e => e.DeliveryDateUtc).HasColumnType("datetime");

			entity.HasOne(d => d.Bidding).WithMany(p => p.BiddingProposals)
				.HasForeignKey(d => d.BiddingId)
				.HasConstraintName("FK_BiddingProposals_Biddings");

			entity.HasOne(d => d.DeliveryType).WithMany(p => p.BiddingProposals)
				.HasForeignKey(d => d.DeliveryTypeId)
				.HasConstraintName("FK_BiddingProposals_DeliveryTypes");
		});

		modelBuilder.Entity<BiddingProposalItem>(entity =>
		{
			entity.HasOne(d => d.BiddingProposal).WithMany(p => p.BiddingProposalItems)
				.HasForeignKey(d => d.BiddingProposalId)
				.HasConstraintName("FK_BiddingProposalItems_BiddingProposals");

			entity.HasOne(d => d.JobItem).WithMany(p => p.BiddingProposalItems)
				.HasForeignKey(d => d.JobItemId)
				.HasConstraintName("FK_BiddingProposalItems_JobItems");
		});

		modelBuilder.Entity<CargoMode>(entity =>
		{
			entity.Property(e => e.Name)
				.HasMaxLength(30)
				.IsUnicode(false);
		});

		modelBuilder.Entity<City>(entity =>
		{
			entity.Property(e => e.CityName)
				.HasMaxLength(100)
				.IsUnicode(false);

			entity.HasOne(d => d.Country).WithMany(p => p.Cities)
				.HasForeignKey(d => d.CountryId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("FK_Cities_Countries");
		});

		modelBuilder.Entity<Country>(entity =>
		{
			entity.Property(e => e.CountryName)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<CourierMode>(entity =>
		{
			entity.Property(e => e.Name)
				.HasMaxLength(30)
				.IsUnicode(false);
		});

		modelBuilder.Entity<DeliveryType>(entity =>
		{
			entity.HasKey(e => e.Id).HasName("PK_DeliveryOptions");

			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<ItemType>(entity =>
		{
			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<Job>(entity =>
		{
			entity.HasIndex(e => e.JobNumber, "IX_Jobs").IsUnique();

			entity.Property(e => e.Comments)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.CreatedOnUtc).HasColumnType("datetime");
			entity.Property(e => e.DestinationAddress)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.ExpiryDateUtc).HasColumnType("datetime");
			entity.Property(e => e.JobNumber).HasMaxLength(10);
			entity.Property(e => e.OriginAddress)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.PickupDateUtc).HasColumnType("datetime");
			entity.Property(e => e.RecipientContact)
				.HasMaxLength(50)
				.IsUnicode(false);
			entity.Property(e => e.RecipientEmail)
				.HasMaxLength(100)
				.IsUnicode(false);
			entity.Property(e => e.RecipientName)
				.HasMaxLength(250)
				.IsUnicode(false);

			entity.HasOne(d => d.CargoMode).WithMany(p => p.Jobs)
				.HasForeignKey(d => d.CargoModeId)
				.HasConstraintName("FK_Jobs_CargoModes");

			entity.HasOne(d => d.CourierMode).WithMany(p => p.Jobs)
				.HasForeignKey(d => d.CourierModeId)
				.HasConstraintName("FK_Jobs_CourierModes");

			entity.HasOne(d => d.DestinationCity).WithMany(p => p.JobDestinationCities)
				.HasForeignKey(d => d.DestinationCityId)
				.HasConstraintName("FK_Jobs_Cities1");

			entity.HasOne(d => d.DestinationCountry).WithMany(p => p.JobDestinationCountries)
				.HasForeignKey(d => d.DestinationCountryId)
				.HasConstraintName("FK_Jobs_Countries1");

			entity.HasOne(d => d.JobStatus).WithMany(p => p.Jobs)
				.HasForeignKey(d => d.JobStatusId)
				.HasConstraintName("FK_Jobs_JobStatuses");

			entity.HasOne(d => d.OriginCity).WithMany(p => p.JobOriginCities)
				.HasForeignKey(d => d.OriginCityId)
				.HasConstraintName("FK_Jobs_Cities");

			entity.HasOne(d => d.OriginCountry).WithMany(p => p.JobOriginCountries)
				.HasForeignKey(d => d.OriginCountryId)
				.HasConstraintName("FK_Jobs_Countries");
		});

		modelBuilder.Entity<JobItem>(entity =>
		{
			entity.Property(e => e.Description).HasMaxLength(500);
			entity.Property(e => e.Dimensions)
				.HasMaxLength(50)
				.IsUnicode(false);
			entity.Property(e => e.ImageUrl)
				.HasMaxLength(250)
				.IsUnicode(false);
			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);

			entity.HasOne(d => d.ItemType).WithMany(p => p.JobItems)
				.HasForeignKey(d => d.ItemTypeId)
				.HasConstraintName("FK_JobItems_ItemTypes");

			entity.HasOne(d => d.Job).WithMany(p => p.JobItems)
				.HasForeignKey(d => d.JobId)
				.HasConstraintName("FK_JobItems_Jobs");
		});

		modelBuilder.Entity<JobStatus>(entity =>
		{
			entity.Property(e => e.Status)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
