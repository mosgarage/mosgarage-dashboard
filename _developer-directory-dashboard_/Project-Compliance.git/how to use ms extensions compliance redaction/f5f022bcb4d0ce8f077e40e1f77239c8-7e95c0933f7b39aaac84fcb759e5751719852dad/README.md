# How to use Microsoft.Extensions.Compliance.Redaction package

This package provides a set of classes that can be used to redact sensitive information from a string or a stream. Redaction is most commonly used in Logging so that we can remove privacy-sensitive information from logs, but it can also be used in other scenarios like redacting dimensions in Metrics, or headers data when using the new HeaderParsing middlewares.

The process of adding redaction to an application, mostly consists of five steps:

1. Create a data taxonomy for your company/application based on the sensitive information that you want interact with.
2. Apply the resulting classifications to your models.
3. Add Redaction to Dependency Injection container, and optionally configure the HMAC Redactor in case that is the appropriate redaction strategy for your application.
4. Enable Redaction functionality in the logging generator.
5. Optionally, you can enable report generator for auditing purposes.

## Sample

For our sample, we will add redaction to a vanilla ASP.NET Core Web API application. This application was created by simply running `dotnet new webapi` on a folder.

### 1. Create a data taxonomy

The first step is to create a data taxonomy for your application. This taxonomy will be used to classify the data that you want to redact. For our sample, we will redact the generated WeatherForcast type, for simplicity purposes. In your application, it is likely that you will have models like: User, Customer, Order, etc. that will contain privacy-sensitive information that you'll want to redact. For now, we will keep it simple and just create two taxonomies: PrivateData and OtherData. The first one will be used to classify the data that we want to obfuscate using the HMAC Redactor, and the latter one will be used to classify the data that we just want to use a custom redactor to replace the data with a constant string. To do this, we write the following code to create new taxonomy attributes:

```csharp
using Microsoft.Extensions.Compliance.Classification;

// ....

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

```

### 2. Apply the resulting classifications to your models

Next step, is to apply the resulting classifications to your models. For our sample, we will apply the PrivateDataAttribute to the `Date` property of the WeatherForecast class, and the OtherDataAttribute to the `TemperatureC` property of the same class. We will also leave the `Summary` property without a classification in our example. The modified code looks like this:

```csharp
// notice that the generated code had WeatherForecast as a record, but we changed it to a class due to https://github.com/dotnet/extensions/issues/4657
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
```

### 3. Add Redaction to Dependency Injection container

Next step, is to add Redaction to the Dependency Injection container. In here, we will also define a new custom redactor to showcase how to do it, but this is of course not required in case you just want to use the built-in redactors. To define a custom redactor, you can do the following:

```csharp
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
```

This redactor will simply replace the input with a constant string of four stars. Now, we can add the redactor to the Dependency Injection container, along with configuring the HMAC Redactor. To do this, we add the following code before the call of `builder.Build()`:

```csharp
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
```

In the above, we are adding redaction to the DI container, and we are configuring the HMac Redactor to be used when redacting Data annotated as PrivateData. The HMac Redactor is an obfuscator that uses a configured (secret!) key in order to obfuscate the data. This will allow you to correlate log entries that have been redacted, since the same input will generate the same output, but will also make it so that people with access to the logs cannot easily view the original input data without the access of the private key. In the above, the key is just a string in the code, but this should be a base64 string of at least 44 characters long, and should come from a secure store like KeyVault. Finally, we are also configuring to use MyCustomRedactor for data annotated as OtherData. This redactor will simply replace the data with a constant string of four stars (which won't allow correlation as any input will always generate a constant output).

### 4. Enable Redaction functionality in the logging generator

Next step, is to enable Redaction functionality in the logging generator. To do this, we first have to enable the redaction engine on the logging generator:

```csharp
builder.Services.AddLogging(logging => 
{
    logging.EnableRedaction();
    logging.AddJsonConsole(); //Enable structure logs on the console to view the redacted data.
});
```

And we also need to start using the new logging generator which will automatically perform the redaction for us. The LogProperties attribute will generate structured logs and will apply the right redaction to each property:

```csharp
public static partial class Log
{
    [LoggerMessage(1, LogLevel.Warning, "Returned WeatherForecast: {weatherForecast}")]
    public static partial void LogWeatherForecast(ILogger logger, [LogProperties] WeatherForecast weatherForecast);
}
```

Finally, we call the logger from the delegate handling the GET request:

```csharp
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
```

### 5. Optionally, you can enable report generator for auditing purposes

Finally, you can optionally enable report generator for auditing purposes which will help you understand what are the different data classifications applied to your application and how are they being redacted. To do this, just add a reference to the `Microsoft.Extensions.AuditReports` package.

## Running the sample

<details>
  <summary>Click to view the full code.</summary>

```csharp
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
```

</details>

When running the sample and sending a request to the `/weatherforecast` endpoint, you should see something like this on the console:

```console
{"EventId":1,"LogLevel":"Warning","Category":"Program","Message":"Returned WeatherForecast: WeatherForecast","State":{"Message":"Microsoft.Extensions.Logging.ExtendedLogger\u002BModernTagJoiner","{OriginalFormat}":"Returned WeatherForecast: {weatherForecast}","weatherForecast.TemperatureF":51,"weatherForecast.Summary":"Warm","weatherForecast":"WeatherForecast","weatherForecast.TemperatureC":"****","weatherForecast.Date":"19:NH5YoL2zdO8vZ9d2yt1v0g=="}}
{"EventId":1,"LogLevel":"Warning","Category":"Program","Message":"Returned WeatherForecast: WeatherForecast","State":{"Message":"Microsoft.Extensions.Logging.ExtendedLogger\u002BModernTagJoiner","{OriginalFormat}":"Returned WeatherForecast: {weatherForecast}","weatherForecast.TemperatureF":75,"weatherForecast.Summary":"Scorching","weatherForecast":"WeatherForecast","weatherForecast.TemperatureC":"****","weatherForecast.Date":"19:X2HoZflp2xhXohFrwZm\u002BNA=="}}
{"EventId":1,"LogLevel":"Warning","Category":"Program","Message":"Returned WeatherForecast: WeatherForecast","State":{"Message":"Microsoft.Extensions.Logging.ExtendedLogger\u002BModernTagJoiner","{OriginalFormat}":"Returned WeatherForecast: {weatherForecast}","weatherForecast.TemperatureF":102,"weatherForecast.Summary":"Freezing","weatherForecast":"WeatherForecast","weatherForecast.TemperatureC":"****","weatherForecast.Date":"19:ALS\u002BEzyd5sWuNvGEpN9dRQ=="}}
{"EventId":1,"LogLevel":"Warning","Category":"Program","Message":"Returned WeatherForecast: WeatherForecast","State":{"Message":"Microsoft.Extensions.Logging.ExtendedLogger\u002BModernTagJoiner","{OriginalFormat}":"Returned WeatherForecast: {weatherForecast}","weatherForecast.TemperatureF":7,"weatherForecast.Summary":"Scorching","weatherForecast":"WeatherForecast","weatherForecast.TemperatureC":"****","weatherForecast.Date":"19:oBPaowcQp7qc/KzxRWkcgg=="}}
{"EventId":1,"LogLevel":"Warning","Category":"Program","Message":"Returned WeatherForecast: WeatherForecast","State":{"Message":"Microsoft.Extensions.Logging.ExtendedLogger\u002BModernTagJoiner","{OriginalFormat}":"Returned WeatherForecast: {weatherForecast}","weatherForecast.TemperatureF":37,"weatherForecast.Summary":"Chilly","weatherForecast":"WeatherForecast","weatherForecast.TemperatureC":"****","weatherForecast.Date":"19:9MLsctBavRgsTWIRZW2Ohg=="}}
```

As you can see above, the Date property is being obfuscated using the HMacRedactor, the TemperatureC is being redacted using our custom redactor, and the Summary property is not being redacted as it is not annotated.
