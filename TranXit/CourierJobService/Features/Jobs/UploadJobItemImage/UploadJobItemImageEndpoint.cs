using Carter;
using CourierJobService.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace CourierJobService.Features.Jobs.UploadJobItemImage;

public class UploadJobItemImageEndpoint : CarterModule
{
	public UploadJobItemImageEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/jobs/job-item-image", async (HttpRequest request, ISender sender) =>
		{
			var form = await request.ReadFormAsync();
			var jobItemId = Convert.ToInt32(form["JobItemId"]);
			var file = form.Files.GetFile("File");
			if (file is null || file.Length == 0)
			{
				return Results.BadRequest("No file uploaded.");
			}
			var command = new UploadJobItemImage.Command
			{
				JobItemId = jobItemId,
				File = file
			};
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
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
		.Produces<Result<ImageResult>>((int)HttpStatusCode.BadRequest);
	}

}
public class UploadJobItemImage
{
	public class Command : IRequest<Result<ImageResult>>
	{
		public required int JobItemId { get; set; }
		public required IFormFile File { get; set; }
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.File)
				.Must(x => x.Length > 0).WithMessage("Invalid File");
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

			var jobItem = await courierJobDbContext.JobItems.FindAsync(request.JobItemId);
			if (jobItem is null)
			{
				return new Error("Invalid Item");
			}
			var jobItemImage = await courierJobDbContext.JobItemImages
				.SingleOrDefaultAsync(x => x.JobItemId == request.JobItemId);
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
				await request.File.CopyToAsync(ms);

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
			await courierJobDbContext.JobItemImages.AddAsync(jobItemImage);
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