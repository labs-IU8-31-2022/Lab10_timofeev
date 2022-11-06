using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System;

namespace yahoo;

internal static class Yahoo
{
    public static string GetData(string quotation)
    {
        HttpClient request = new();
        request.BaseAddress = new Uri($"https://query1.finance.yahoo.com/v7/finance/download/{quotation}");
        var response =
            request.GetAsync($"?period1={DateTimeOffset.Now.AddDays(-7).ToUnixTimeSeconds()}" +
                             $"&period2={DateTimeOffset.Now.ToUnixTimeSeconds()}" +
                             "&interval=1d&events=history&includeAdjustedClose=true").Result;
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().Result;
    }

    public static IEnumerable<decimal>? TwoDays(string data)
    {
        if (data == "")
        {
            return null;
        }

        var prices = data
            .Split('\n')
            .Reverse()
            .Select(line => line.Split(','))
            .Where(numbers => !numbers.Contains("null") && !numbers.Contains("Date"))
            .Select(numbers => Convert.ToDecimal(numbers[4]))
            .Take(2);

        return prices;
    }
}