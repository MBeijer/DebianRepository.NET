using DebianRepository.Services;
using Microsoft.AspNetCore.Http.Features;

namespace DebianRepository;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		// Add services to the container.

		builder.Services.AddControllers();
		// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
		builder.Services.AddOpenApi();
		builder.Services.AddSingleton<DebRepoService>();
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();

		builder.WebHost.ConfigureKestrel(options =>
		{
			options.Limits.MaxRequestBodySize = 200000000; // 100 MB
		});

		builder.Services.Configure<IISServerOptions>(options =>
		{
			options.MaxRequestBodySize = 200000000;
		});

		builder.Services.Configure<FormOptions>(options =>
		{
			options.MultipartBodyLengthLimit = 200000000;
		});


		var app = builder.Build();

		var startup = new Startup(app.Configuration);
		startup.Configure(app, app.Environment);

		app.Run();
	}
}