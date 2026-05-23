using AccountService.Database;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using SharedServicesManager.Helpers;
using System.Net;

namespace AccountService.Features.AccountDocuments.UploadDocument;

public class UploadDocumentEndpoint : CarterModule
{
	public UploadDocumentEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/upload/document", async (HttpRequest request, ISender sender, IHttpContextAccessor httpContextAccessor) =>
		{
			var form = await request.ReadFormAsync();
			var userId = HttpContextUser.GetCurrentUserId(httpContextAccessor);
			var formUserId = form["UserId"].FirstOrDefault();
			if (int.TryParse(formUserId, out var requestedUserId) && requestedUserId != userId)
			{
				return Results.Forbid();
			}

			var files = form.Files.GetFiles("Files");
			if (files is null || files.Count == 0)
			{
				return Results.BadRequest("No file uploaded.");
			}
			var command = new UploadDocument.Command
			{
				UserId = userId,
				Files = files
			};
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		})
		.RequireAuthorization()
		.WithTags("Auth")
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
										["Files"] = new Microsoft.OpenApi.Models.OpenApiSchema
										{
											Type = "array",
											Items = new Microsoft.OpenApi.Models.OpenApiSchema
												{
													Type = "string",
													Format = "binary",
													Description = "The files to upload"
												},
											Description = "The files to upload"
										}
									},
									Required = new HashSet<string> { "Files" }
								}
						}
				}
			};
			return operation;
		})
		.Produces<Result<List<int>>>((int)HttpStatusCode.OK)
		.Produces<Result<List<int>>>((int)HttpStatusCode.BadRequest);
	}
}
public class UploadDocument
{
	private const int MaxFileCount = 5;
	private const long MaxFileBytes = 10 * 1024 * 1024;

	public class Command : IRequest<Result<List<int>>>
	{
		public required int UserId { get; set; }
		public required IReadOnlyList<IFormFile> Files { get; set; }
	}

	public class Validator : AbstractValidator<Command>
	{
		public Validator()
		{
			RuleFor(c => c.Files)
				.Must(x => x.Count > 0).WithMessage("Invalid File")
				.Must(x => x.Count <= MaxFileCount).WithMessage("A maximum of 5 files can be uploaded")
				.Must(x => x.All(file => file.Length > 0 && file.Length <= MaxFileBytes))
				.WithMessage("Each file must be 10MB or less");
		}
	}
	internal sealed class Handler(AccountDbContext accountDbContext,
		IValidator<Command> validator)
		: IRequestHandler<Command, Result<List<int>>>
	{
		public async Task<Result<List<int>>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}

			var user = await accountDbContext.Users.FindAsync([request.UserId], cancellationToken);
			if (user is null)
			{
				return new Error("Invalid User");
			}
			var userFiles = await accountDbContext.UserFiles
				.Where(x=> x.UserId == request.UserId)
				.ToListAsync(cancellationToken);
			if (userFiles.Any())
			{
				accountDbContext.UserFiles.RemoveRange(userFiles);
				await accountDbContext.SaveChangesAsync(cancellationToken);
			}
			userFiles = new List<UserFile>();
			foreach (var file in request.Files)
			{
				var userFile = new UserFile
				{
					Name = file.FileName,
					Type = file.ContentType,
					UserId = request.UserId
				};
				using (MemoryStream ms = new MemoryStream())
				{
					// copy the file to memory stream 
					await file.CopyToAsync(ms, cancellationToken);

					// set the byte array 
					var fileBytes = ms.ToArray();
					userFile.Content = Convert.ToBase64String(fileBytes);
				}
				userFiles.Add(userFile);
			}
			await accountDbContext.UserFiles.AddRangeAsync(userFiles);
			await accountDbContext.SaveChangesAsync(cancellationToken);
			return userFiles.Select(x => x.Id).ToList();
		}
	}
}
