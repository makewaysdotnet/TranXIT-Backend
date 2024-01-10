using AccountService.Database;
using Carter;
using FluentValidation;
using Mapster;
using MediatR;
using SharedServicesManager;
using System.Security.Claims;

namespace AccountService.Features.AccountDocuments.UploadDocument;

public class UploadDocumentEndpoint : CarterModule
{
	public UploadDocumentEndpoint()
		: base("/api")
	{ }
	public override void AddRoutes(IEndpointRouteBuilder app)
	{
		app.MapPost("/upload", async (IFormFile file, ISender sender) =>
		{
			var command = new UploadDocument.Command { File = file };
			var result = await sender.Send(command);
			if (!result.isSuccess)
			{
				return Results.BadRequest(result);
			}
			return Results.Ok(result);
		})
		.RequireAuthorization()
		.DisableAntiforgery();
	}
}
public class UploadDocument
{
	public class Command : IRequest<Result<int>>
	{
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
		IValidator<Command> validator,
		IHttpContextAccessor httpContextAccessor)
		: IRequestHandler<Command, Result<int>>
	{
		public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
		{
			var validationResult = await validator.ValidateAsync(request);
			if (!validationResult.IsValid)
			{
				return new Error(validationResult.ToString());
			}
			var userId = httpContextAccessor.HttpContext?.User.FindFirstValue("UserId");
			if (userId is null)
			{
				return new Error("Invalid User");
			}
			var userFile = new UserFile
			{
				Name = request.File.Name,
				Type = request.File.ContentType,
				UserId = int.Parse(userId)
			};
			using (MemoryStream ms = new MemoryStream())
			{
				// copy the file to memory stream 
				await request.File.CopyToAsync(ms);

				// set the byte array 
				var fileBytes = ms.ToArray();
				userFile.Content = Convert.ToBase64String(fileBytes);
			}
			accountDbContext.UserFiles.Add(userFile);
			await accountDbContext.SaveChangesAsync(cancellationToken);
			return userFile.Id;
		}
	}
}