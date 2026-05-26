using Carter;
using CourierJobService.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace CourierJobService.Features.Jobs.UploadJobItemImage;

public class UploadJobItemImageEndpoint : CarterModule
{
	public UploadJobItemImageEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/jobs/job-item-image", async (HttpRequest request, ISender sender, IHttpContextAccessor httpContext) =>
		{
			var form = await request.ReadFormAsync();
			var formJobItemId = form["JobItemId"].FirstOrDefault();
			if (!int.TryParse(formJobItemId, out var jobItemId))
			{
				return Results.BadRequest("Invalid JobItemId.");
			}

			var file = form.Files.GetFile("File");
			if (file is null || file.Length == 0)
			{
				return Results.BadRequest("No file uploaded.");
			}
			var command = new UploadJobItemImage.Command
			{
				JobItemId = jobItemId,
				CurrentUserId = HttpContextUser.GetCurrentUserId(httpContext),
				File = file
			};
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				if (result.error.Contains(UploadJobItemImage.ForbiddenError))
				{
					return Results.Forbid();
				}
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).RequireAuthorization("CustomerPolicy")
		.WithTags("Jobs")
		.WithOpenApi(operation =>
		{
			operation.OperationId = "UploadJobItemImage";
			operation.Summary = "Uploads a Job Item Image.";
			operation.RequestBody = new Microsoft.OpenApi.Models.OpenApiRequestBody
			{
				Content =
				{
					["multipart/form-data"] = new Microsoft.OpenApi.Models.OpenApiMediaType
						{
							Schema = new Microsoft.OpenApi.Models.OpenApiSchema
								{
									Type = "object",
									Properties =
									{
										["JobItemId"] = new Microsoft.OpenApi.Models.OpenApiSchema
										{
											Type = "string",
											Description = "Job Item ID"
										},
										["File"] = new Microsoft.OpenApi.Models.OpenApiSchema
										{
											Type = "string",
											Format = "binary",
											Description = "The image to upload"
										}
									},
									Required = new HashSet<string> { "JobItemId", "File" }
								}
						}
				}
			};
			return operation;
		})
		.Produces<Result<ImageResult>>((int)HttpStatusCode.OK)
		.Produces<Result<ImageResult>>((int)HttpStatusCode.BadRequest)
		.Produces((int)HttpStatusCode.Forbidden);
	}

}
public class UploadJobItemImage
{
	public const string ForbiddenError = "Forbidden";

	private const long MaxImageBytes = 5 * 1024 * 1024;
	private static readonly string[] AllowedImageContentTypes =
	[
		"image/jpeg",
		"image/png",
		"image/webp"
	];

	public class Command : IRequest<Result<ImageResult>>
	{
		public required int JobItemId { get; set; }
		public required int CurrentUserId { get; set; }
		public required IFormFile File { get; set; }
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.File)
				.Must(x => x.Length > 0).WithMessage("Invalid File")
				.Must(x => x.Length <= MaxImageBytes).WithMessage("Image size must be 5MB or less")
				.Must(x => AllowedImageContentTypes.Contains(x.ContentType, StringComparer.OrdinalIgnoreCase))
				.WithMessage("Only JPEG, PNG, and WebP images are allowed");
		}
	}
	internal sealed class Handler(CourierJobDbContext courierJobDbContext,
		IValidator<Command> validator)
		: IRequestHandler<Command, Result<ImageResult>>
	{
		public async Task<Result<ImageResult>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			var jobItem = await courierJobDbContext.JobItems
				.Include(x => x.Job)
				.FirstOrDefaultAsync(x => x.Id == request.JobItemId, cancellationToken);
			if (jobItem?.Job is null)
			{
				return new Error("Invalid Item");
			}
			if (jobItem.Job.UserId != request.CurrentUserId)
			{
				return new Error(ForbiddenError);
			}
			var jobItemImage = await courierJobDbContext.JobItemImages
				.SingleOrDefaultAsync(x => x.JobItemId == request.JobItemId, cancellationToken);
			var isUpdate = false;
			if (jobItemImage is not null)
			{
				jobItemImage.Name = request.File.FileName;
				jobItemImage.Type = request.File.ContentType;
				isUpdate = true;
			}
			else
			{
				jobItemImage = new JobItemImage
				{
					Name = request.File.FileName,
					Type = request.File.ContentType,
					JobItemId = request.JobItemId
				};
			}

			using (MemoryStream ms = new MemoryStream())
			{
				// copy the file to memory stream 
				await request.File.CopyToAsync(ms, cancellationToken);

				// set the byte array 
				var fileBytes = ms.ToArray();
				jobItemImage.Content = Convert.ToBase64String(fileBytes);
			}
			if (isUpdate)
			{
				courierJobDbContext.JobItemImages.Update(jobItemImage);
				await courierJobDbContext.SaveChangesAsync(cancellationToken);
				return new ImageResult
				{
					Id = jobItemImage.JobItemId,
					Name = jobItemImage.Name,
					Type = jobItemImage.Type,
					Content = jobItemImage.Content
				};
			}
			await courierJobDbContext.JobItemImages.AddAsync(jobItemImage, cancellationToken);
			await courierJobDbContext.SaveChangesAsync(cancellationToken);
			return new ImageResult
			{
				Id = jobItemImage.JobItemId,
				Name = jobItemImage.Name,
				Type = jobItemImage.Type,
				Content = jobItemImage.Content
			};
		}
	}
}
