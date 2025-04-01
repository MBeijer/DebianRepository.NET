using System.Text;
using DebianRepository.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

namespace DebianRepository;

public static class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		var jwtSecret = builder.Configuration["JWT_SECRET"] ?? throw new("JWT_SECRET is required");

		builder.WebHost.ConfigureKestrel(options =>
		{
			options.Limits.MaxRequestBodySize = 500000000; // 100 MB
		});

		builder.Services.Configure<IISServerOptions>(options =>
		{
			options.MaxRequestBodySize = 500000000;
		});

		builder.Services.Configure<FormOptions>(options =>
		{
			options.MultipartBodyLengthLimit = 500000000;
		});


		builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
		       .AddJwtBearer(options =>
		       {
			       options.TokenValidationParameters = new()
			       {
				       ValidateIssuer           = false,
				       ValidateAudience         = false,
				       ValidateLifetime         = true,
				       ValidateIssuerSigningKey = true,
				       IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
			       };
		       });

		builder.Services.AddAuthorization();
		builder.Services.AddControllers();
		builder.Services.AddSingleton<DebRepoService>();
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();

		var app = builder.Build();

		if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}

		app.UseRouting();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();

		app.Run();
	}
}