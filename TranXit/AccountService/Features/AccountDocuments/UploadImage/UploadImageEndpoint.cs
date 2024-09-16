using AccountService.Database;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.AccountDocuments.UploadImage;

public class UploadImageEndpoint : CarterModule
{
	public UploadImageEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/upload/image", async (HttpRequest request, ISender sender) =>
		{
			var form = await request.ReadFormAsync();
			var userId = Convert.ToInt32(form["UserId"]);
			var file = form.Files.GetFile("File");
			if (file is null || file.Length == 0)
			{
				return Results.BadRequest("No file uploaded.");
			}
			var command = new UploadImage.Command
			{
				UserId = userId,
				File = file
			};
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		}).WithTags("Auth")
		.WithOpenApi(operation =>
		{
			operation.OperationId = "UploadDocument";
			operation.Summary = "Uploads a document.";
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
										["UserId"] = new Microsoft.OpenApi.Models.OpenApiSchema
										{
											Type = "string",
											Description = "User ID"
										},
										["File"] = new Microsoft.OpenApi.Models.OpenApiSchema
										{
											Type = "string",
											Format = "binary",
											Description = "The image to upload"
										}
									},
									Required = new HashSet<string> { "UserId", "File" }
								}
						}
				}
			};
			return operation;
		})
		.Produces<Result<int>>((int)HttpStatusCode.OK)
		.Produces<Result<int>>((int)HttpStatusCode.BadRequest);
	}

}
public class UploadImage
{
	public class Command : IRequest<Result<int>>
	{
		public required int UserId { get; set; }
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
	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator)
		: IRequestHandler<Command, Result<int>>
	{
		public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			var user = await accountDbContext.Users.FindAsync(request.UserId);
			if (user is null)
			{
				return new Error("Invalid User");
			}
			var userImage = await accountDbContext.UserImages
				.SingleOrDefaultAsync(x => x.UserId == request.UserId);
			var isUpdate = false;
			if (userImage is not null)
			{
				userImage.Name = request.File.FileName;
				userImage.Type = request.File.ContentType;
				userImage.UserId = request.UserId;
				isUpdate = true;
			}
			else
			{
				userImage = new UserImage
				{
					Name = request.File.FileName,
					Type = request.File.ContentType,
					UserId = request.UserId
				};
			}

			using (MemoryStream ms = new MemoryStream())
			{
				// copy the file to memory stream 
				await request.File.CopyToAsync(ms);

				// set the byte array 
				var fileBytes = ms.ToArray();
				userImage.Content = Convert.ToBase64String(fileBytes);
			}
			if (isUpdate)
			{
				accountDbContext.UserImages.Update(userImage);
				await accountDbContext.SaveChangesAsync(cancellationToken);
				return userImage.Id;
			}
			await accountDbContext.UserImages.AddAsync(userImage);
			await accountDbContext.SaveChangesAsync(cancellationToken);
			return userImage.Id;
		}
	}
}