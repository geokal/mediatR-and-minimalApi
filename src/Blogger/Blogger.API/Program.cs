using Blogger.API.Models.DataTransferObjects;
using Blogger.API.Repository;
using Blogger.API.Requests.Commands;
using Blogger.API.Requests.Queries;
using Blogger.API.Services;
using MediatR;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;


Log.Logger = new LoggerConfiguration()
    .WriteTo.Async(a => a.Console())
    .CreateLogger();

try
{
    Log.Information("starting server.");
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, loggerConfiguration) =>
    {
        loggerConfiguration.WriteTo.Console();
        loggerConfiguration.ReadFrom.Configuration(context.Configuration);
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddSingleton<IBlogRepository, BlogRepository>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddMemoryCache();
    builder.Services.AddAutoMapper(typeof(Program));
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

    // Build a resource configuration action to set service information.
    // Action<ResourceBuilder> configureResource = r => r.AddService(
    // serviceName: "otel-test",
    // serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    // serviceInstanceId: Environment.MachineName);
    
    builder.Services.AddOpenTelemetry()
    //.ConfigureResource(configureResource)
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    })
    .WithMetrics(metrics => metrics
          .AddRuntimeInstrumentation()
          // Metrics provides by ASP.NET Core in .NET 8
        .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http")
    );

    var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    if (useOtlpExporter)
    {
        builder.Services.AddOpenTelemetry().UseOtlpExporter();
    }

    var app = builder.Build();

    #region MinimalAPIs

    app.MapGet("/blogs/{id:guid}", async (IMediator mediator, Guid id) => 
            await mediator.Send(new GetBlogByIdQuery(id)) is var blog 
                ? Results.Ok(blog) 
                : Results.NotFound())
        .WithName("BlogById");

    app.MapPost("/blogs", async (IMediator mediator, CreateBlogRequest request) =>
    {
        var newBlog = await mediator.Send(new CreateBlogCommand(request));
        return Results.CreatedAtRoute("BlogById", new { id = newBlog.Id }, newBlog);
    });

    #endregion

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    //app.UseOpenTelemetryPrometheusScrapingEndpoint();
    // app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
