using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

public static class WeatherTools
{
    private static readonly HttpClient Http = new();

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(GetCurrentWeather, "GetCurrentWeather",
            "Gets current weather conditions for a given location using the Open-Meteo API. Returns raw JSON with temperature, wind speed, humidity, and weather code. No API key required.");

        yield return AIFunctionFactory.Create(GetWeatherForecast, "GetWeatherForecast",
            "Gets a 7-day weather forecast for a given location using the Open-Meteo API. Returns raw JSON with daily min/max temperatures, precipitation, and weather codes. No API key required.");
    }

    private static async Task<string> GetCurrentWeather(
        [Description("Latitude of the location (e.g. 40.71 for New York)")] string latitude,
        [Description("Longitude of the location (e.g. -74.01 for New York)")] string longitude,
        [Description("Temperature unit: celsius or fahrenheit (default: celsius)")] string? temperatureUnit)
    {
        var unit = string.Equals(temperatureUnit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
            ? "fahrenheit" : "celsius";

        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
                  $"&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m" +
                  $"&temperature_unit={unit}&wind_speed_unit=kmh&timezone=auto";

        var json = await Http.GetStringAsync(url);
        return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n{json}";
    }

    private static async Task<string> GetWeatherForecast(
        [Description("Latitude of the location (e.g. 40.71 for New York)")] string latitude,
        [Description("Longitude of the location (e.g. -74.01 for New York)")] string longitude,
        [Description("Temperature unit: celsius or fahrenheit (default: celsius)")] string? temperatureUnit)
    {
        var unit = string.Equals(temperatureUnit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
            ? "fahrenheit" : "celsius";

        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
                  $"&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode,wind_speed_10m_max" +
                  $"&temperature_unit={unit}&wind_speed_unit=kmh&timezone=auto";

        var json = await Http.GetStringAsync(url);
        return $"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n{json}";
    }
}
