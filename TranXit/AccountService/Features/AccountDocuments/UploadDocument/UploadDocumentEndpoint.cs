using AccountService.Database;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedServicesManager;
using System.Net;

namespace AccountService.Features.AccountDocuments.UploadDocument;

public class UploadDocumentEndpoint : CarterModule
{
	public UploadDocumentEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/upload/document", async (HttpRequest request, ISender sender) =>
		{
			var form = await request.ReadFormAsync();
			var userId = Convert.ToInt32(form["UserId"]);
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
										["UserId"] = new Microsoft.OpenApi.Models.OpenApiSchema
										{
											Type = "string",
											Description = "User ID"
										},
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
									Required = new HashSet<string> { "UserId", "Files" }
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
				.Must(x => x.Count > 0).WithMessage("Invalid File");
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

			var user = await accountDbContext.Users.FindAsync(request.UserId);
			if (user is null)
			{
				return new Error("Invalid User");
			}
			var userFiles = await accountDbContext.UserFiles
				.Where(x=> x.UserId == request.UserId)
				.ToListAsync();
			if (userFiles.Any())
			{
				accountDbContext.UserFiles.RemoveRange(userFiles);
				await accountDbContext.SaveChangesAsync();
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
					await file.CopyToAsync(ms);

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