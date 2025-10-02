using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

#pragma warning disable EXTEXP0002 // HMac redactor is experimental so we need to disable this warning given we are using it.
builder.Services.AddRedaction(configure =>
{
    // For Private Data, we will configure to use the HMac redactor which will allow correlation between log entries.
    configure.SetHmacRedactor(configureHmac =>
    {
        // This key should be kept SECRET! It should be fetched from keyvault or some other secure store.
        configureHmac.Key = "YWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFh";
        // Some discriminator to differentiate between different deployments of a service.
        configureHmac.KeyId = 19;

    }, new DataClassificationSet(DataClassifications.PrivateDataClassification));

    // For Other Data, we will configure to use a custom redactor which will replace the data with ****.
    configure.SetRedactor<MyCustomRedactor>(new DataClassificationSet(DataClassifications.OtherDataClassification));
});
#pragma warning restore EXTEXP0002 // HMac redactor is experimental so we need to disable this warning given we are using it.

builder.Services.AddLogging(logging => 
{
    logging.EnableRedaction();
    logging.AddJsonConsole(); //Enable structure logs on the console to view the redacted data.
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", ([FromServices] ILogger<Program> logger) =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    foreach(var f in forecast)
    {
        logger.LogWeatherForecast(f);
    }
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

#region Models
public class WeatherForecast
{
    public WeatherForecast(DateOnly date, int temperatureC, string? summary)
    {
        Date = date;
        TemperatureC = temperatureC;
        Summary = summary;
    }

    [PrivateData]
    public DateOnly Date { get; }
    [OtherData]
    public int TemperatureC { get; }
    public string? Summary { get; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
#endregion


#region Data Taxonomy definition
public static class DataClassifications
{
    public static DataClassification PrivateDataClassification {get;} = new DataClassification("PrivateDataTaxonomy", "PrivateData");

    public static DataClassification OtherDataClassification {get;} = new DataClassification("OtherDataTaxonomy", "OtherData");
}

public class PrivateDataAttribute : DataClassificationAttribute
{
    public PrivateDataAttribute() : base(DataClassifications.PrivateDataClassification) { }
}

public class OtherDataAttribute : DataClassificationAttribute
{
    public OtherDataAttribute() : base(DataClassifications.OtherDataClassification) { }
}
#endregion

#region Custom Redactor definition
public class MyCustomRedactor : Redactor
{
    private const string Stars = "****";

    public override int GetRedactedLength(ReadOnlySpan<char> input) => Stars.Length;

    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        Stars.CopyTo(destination);
        return Stars.Length;
    }
}
#endregion

#region Logging Extensions

public static partial class Log
{
    [LoggerMessage(1, LogLevel.Warning, "Returned WeatherForecast: {weatherForecast}")]
    public static partial void LogWeatherForecast(this ILogger logger, [LogProperties] WeatherForecast weatherForecast);
}
#endregion