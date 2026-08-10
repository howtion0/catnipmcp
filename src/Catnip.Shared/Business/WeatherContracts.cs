namespace Catnip.Shared.Business;

public sealed record GetWeatherInput(string City);

public sealed record WeatherData(
    string City,
    string Condition,
    decimal? TemperatureC,
    string Source,
    DateTimeOffset ObservedAt);
