using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Quartz;
using StocksReportingLibrary.Infrastructure;
using Wolverine;
using Wolverine.Http;
using StocksReportingLibrary.Infrastructure.Email;
using StocksReportingLibrary.Configuration;
using StocksReportingLibrary;
using StocksReportingLibrary.Application.Services.Scheduling;
using StocksReportingLibrary.Application.Services.Email;
using Polly;
using Polly.Retry;
using StocksReportingLibrary.Application.Services.File;
using StocksReportingLibrary.Infrastructure.File;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVercel",
        policy => policy.WithOrigins("https://software-quality-team4-wine.vercel.app")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(builder.Configuration);
builder.Services.AddOptions<EmailSettings>()
                .BindConfiguration(EmailSettings.Path)
                .ValidateDataAnnotations()
                .ValidateOnStart();
builder.Services.AddOptions<ReportSettings>()
                .BindConfiguration(ReportSettings.Path)
                .ValidateDataAnnotations()
                .ValidateOnStart();

builder.Services.AddSwaggerGen(c =>
{

    c.CustomSchemaIds(s => s.FullName.Replace("+", "."));
    c.SwaggerDoc("v1", new OpenApiInfo() { Title = "Stocks Reporting", Version = "v1" });
});

builder.Services.AddResiliencePipeline("MailRetryPolicy", pipeline =>
{
    pipeline.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(3),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true
    });
});

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("CreateReportJob");

    q.AddJob<CreateReportJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("CreateReportJobTrigger")
        .WithCronSchedule("0 0 0 ? * MON"));

    var emailJobKey = new JobKey("EmailJob");
    q.AddJob<SendQueuedEmailsJob>(opts => opts.WithIdentity(emailJobKey));
    q.AddTrigger(opts => opts
        .ForJob(emailJobKey)
        .WithIdentity("EmailJobTrigger")
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromMinutes(1))
        .RepeatForever()));

});

builder.Services.AddQuartzHostedService();

var dbString = builder.Configuration.GetConnectionString("DbString");
builder.Services.InstallStocksReportingLibrary(dbString!);

builder.Host.UseWolverine();

builder.Services.AddLogging();
builder.Services.AddHttpClient();

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IEmailQueue, EmailQueueService>();
builder.Services.AddScoped<IReportEmailService, ReportEmailService>();
builder.Services.AddScoped<IDownloader, CsvDownloader>();
builder.Services.AddScoped<StocksReportingLibrary.Application.Services.File.IWriter, StocksReportingLibrary.Infrastructure.File.CsvWriter>();
builder.Services.AddScoped<StocksReportingLibrary.Application.Services.File.IParser, StocksReportingLibrary.Infrastructure.File.CsvParser>();

var app = builder.Build();

app.UseCors("AllowVercel"); 

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StocksReportingDbContext>();
    dbContext.Database.Migrate();
}


app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Stocks Reporting");


});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapWolverineEndpoints();

app.MapFallbackToFile("/index.html");

app.Run();
