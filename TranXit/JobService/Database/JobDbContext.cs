using Microsoft.EntityFrameworkCore;

namespace JobService.Database;

public partial class JobDbContext : DbContext
{
	public JobDbContext()
	{
	}

	public JobDbContext(DbContextOptions<JobDbContext> options)
		: base(options)
	{
	}

	public virtual DbSet<Bidding> Biddings { get; set; }

	public virtual DbSet<BiddingCharge> BiddingCharges { get; set; }

	public virtual DbSet<BiddingDocument> BiddingDocuments { get; set; }

	public virtual DbSet<BiddingProposal> BiddingProposals { get; set; }

	public virtual DbSet<City> Cities { get; set; }

	public virtual DbSet<ContainerSize> ContainerSizes { get; set; }

	public virtual DbSet<Country> Countries { get; set; }

	public virtual DbSet<DeliveryType> DeliveryTypes { get; set; }

	public virtual DbSet<IncoTerm> IncoTerms { get; set; }

	public virtual DbSet<Job> Jobs { get; set; }

	public virtual DbSet<JobContainer> JobContainers { get; set; }

	public virtual DbSet<JobItem> JobItems { get; set; }

	public virtual DbSet<JobStatus> JobStatuses { get; set; }

	public virtual DbSet<Port> Ports { get; set; }

	public virtual DbSet<TransportType> TransportTypes { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Bidding>(entity =>
		{
			entity.HasIndex(e => new { e.UserId, e.JobId }, "UK_Biddings").IsUnique();

			entity.Property(e => e.CargoType)
				.HasMaxLength(10)
				.IsUnicode(false);
			entity.Property(e => e.ScheduleEndDate).HasColumnType("datetime");
			entity.Property(e => e.ScheduleStartDate).HasColumnType("datetime");
			entity.Property(e => e.TransitTime)
				.HasMaxLength(30)
				.IsUnicode(false);

			entity.HasOne(d => d.DestinationPort).WithMany(p => p.BiddingDestinationPorts)
				.HasForeignKey(d => d.DestinationPortId)
				.HasConstraintName("FK_Biddings_Ports");

			entity.HasOne(d => d.IncoTerm).WithMany(p => p.Biddings)
				.HasForeignKey(d => d.IncoTermId)
				.HasConstraintName("FK_Biddings_IncoTerms");

			entity.HasOne(d => d.Job).WithMany(p => p.Biddings)
				.HasForeignKey(d => d.JobId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("FK_Biddings_Jobs");

			entity.HasOne(d => d.OriginPort).WithMany(p => p.BiddingOriginPorts)
				.HasForeignKey(d => d.OriginPortId)
				.HasConstraintName("FK_Biddings_Ports1");
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

		modelBuilder.Entity<BiddingDocument>(entity =>
		{
			entity.Property(e => e.Description)
				.HasMaxLength(200)
				.IsUnicode(false);
			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);

			entity.HasOne(d => d.Bidding).WithMany(p => p.BiddingDocuments)
				.HasForeignKey(d => d.BiddingId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("FK_BiddingDocuments_Biddings");
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

		modelBuilder.Entity<ContainerSize>(entity =>
		{
			entity.Property(e => e.Size)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<Country>(entity =>
		{
			entity.Property(e => e.CountryName)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<DeliveryType>(entity =>
		{
			entity.HasKey(e => e.Id).HasName("PK_DeliveryOptions");

			entity.Property(e => e.Name)
				.HasMaxLength(50)
				.IsUnicode(false);
		});

		modelBuilder.Entity<IncoTerm>(entity =>
		{
			entity.HasKey(e => e.Id).HasName("PK_EncoTerm");

			entity.Property(e => e.Description)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.Name)
				.HasMaxLength(100)
				.IsUnicode(false);
		});

		modelBuilder.Entity<Job>(entity =>
		{
			entity.Property(e => e.CargoReadiness).HasColumnType("datetime");
			entity.Property(e => e.Comments)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.Commodity)
				.HasMaxLength(200)
				.IsUnicode(false);
			entity.Property(e => e.CreatedOn).HasColumnType("datetime");
			entity.Property(e => e.DestinationAddress)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.EstimatedTime).HasColumnType("datetime");
			entity.Property(e => e.Hscode)
				.HasMaxLength(50)
				.IsUnicode(false)
				.HasColumnName("HSCode");
			entity.Property(e => e.OriginAddress)
				.HasMaxLength(500)
				.IsUnicode(false);
			entity.Property(e => e.ShipmentWeight).HasColumnType("decimal(18, 0)");

			entity.HasOne(d => d.DestinationCity).WithMany(p => p.JobDestinationCities)
				.HasForeignKey(d => d.DestinationCityId)
				.HasConstraintName("FK_Jobs_Cities1");

			entity.HasOne(d => d.DestinationCountry).WithMany(p => p.JobDestinationCountries)
				.HasForeignKey(d => d.DestinationCountryId)
				.HasConstraintName("FK_Jobs_Countries");

			entity.HasOne(d => d.DestinationPort).WithMany(p => p.JobDestinationPorts)
				.HasForeignKey(d => d.DestinationPortId)
				.HasConstraintName("FK_Jobs_Ports1");

			entity.HasOne(d => d.IncoTerm).WithMany(p => p.Jobs)
				.HasForeignKey(d => d.IncoTermId)
				.HasConstraintName("FK_Jobs_IncoTerms");

			entity.HasOne(d => d.JobStatus).WithMany(p => p.Jobs)
				.HasForeignKey(d => d.JobStatusId)
				.HasConstraintName("FK_Jobs_JobStatuses");

			entity.HasOne(d => d.OriginCity).WithMany(p => p.JobOriginCities)
				.HasForeignKey(d => d.OriginCityId)
				.HasConstraintName("FK_Jobs_Cities");

			entity.HasOne(d => d.OriginCountry).WithMany(p => p.JobOriginCountries)
				.HasForeignKey(d => d.OriginCountryId)
				.HasConstraintName("FK_Jobs_Countries1");

			entity.HasOne(d => d.OriginPort).WithMany(p => p.JobOriginPorts)
				.HasForeignKey(d => d.OriginPortId)
				.HasConstraintName("FK_Jobs_Ports");
		});

		modelBuilder.Entity<JobContainer>(entity =>
		{
			entity.HasOne(d => d.ContainerSize).WithMany(p => p.JobContainers)
				.HasForeignKey(d => d.ContainerSizeId)
				.HasConstraintName("FK_JobContainers_ContainerSizes");

			entity.HasOne(d => d.Job).WithMany(p => p.JobContainers)
				.HasForeignKey(d => d.JobId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("FK_JobContainers_Jobs");

			entity.HasOne(d => d.TransportType).WithMany(p => p.JobContainers)
				.HasForeignKey(d => d.TransportTypeId)
				.HasConstraintName("FK_JobContainers_TransportTypes");
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

		modelBuilder.Entity<Port>(entity =>
		{
			entity.Property(e => e.Description)
				.HasMaxLength(250)
				.IsUnicode(false);
			entity.Property(e => e.Name)
				.HasMaxLength(100)
				.IsUnicode(false);
		});

		modelBuilder.Entity<TransportType>(entity =>
		{
			entity.Property(e => e.Name)
				.HasMaxLength(30)
				.IsUnicode(false);
		});

		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
