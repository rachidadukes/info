using MemberSearchSpectreTest.Models;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Net.Http.Json;
using System.Text.Json;

public static class MemberSearchApiTestRunner
{
    private const string DefaultUrl = "https://intg.api.nfcu.net/member/v1/members/search";

    public static async Task RunAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        AnsiConsole.Write(
            new FigletText("Member Search")
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[grey]Standalone .NET 9 API validation utility[/]");
        AnsiConsole.MarkupLine("[yellow]Use only approved non-production test data.[/]");
        AnsiConsole.WriteLine();

        var endpoint = configuration["MemberApi:BaseUrl"] ?? DefaultUrl;
        var clientId = configuration["MemberApi:ClientId"];
        var clientSecret = configuration["MemberApi:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            AnsiConsole.MarkupLine(
                "[red]ClientId or ClientSecret is missing from appsettings.json[/]");
            return;
        }

        var searchType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]member search type[/]:")
                .AddChoices("SSN", "First and last name", "Phone number"));

        var request = BuildRequest(searchType);

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-nfcu-clientid", clientId);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-nfcu-clientsecret", clientSecret);
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        HttpResponseMessage? response = null;
        string responseBody = string.Empty;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Calling Member Search API...", async _ =>
                {
                    response = await httpClient.PostAsJsonAsync(endpoint, request, jsonOptions);
                    responseBody = await response.Content.ReadAsStringAsync();
                });

            if (response is null)
            {
                AnsiConsole.MarkupLine("[red]No HTTP response was received.[/]");
                return;
            }

            var statusColor = response.IsSuccessStatusCode ? "green" : "red";
            AnsiConsole.MarkupLine(
                $"HTTP [{statusColor}]{(int)response.StatusCode} {Markup.Escape(response.ReasonPhrase ?? string.Empty)}[/]");
            AnsiConsole.WriteLine();

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                AnsiConsole.MarkupLine("[yellow]The API returned an empty response body.[/]");
                return;
            }

            string displayText;
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                displayText = JsonSerializer.Serialize(document.RootElement, jsonOptions);
            }
            catch (JsonException)
            {
                displayText = responseBody;
            }

            AnsiConsole.Write(
                new Panel(Markup.Escape(displayText))
                    .Header("API Response")
                    .BorderColor(response.IsSuccessStatusCode ? Color.Green : Color.Red)
                    .Expand());

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Verify the API subscription, environment, client credentials, and request data.[/]");
            }
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]HTTP error:[/] {Markup.Escape(ex.Message)}");
        }
        catch (TaskCanceledException ex)
        {
            AnsiConsole.MarkupLine($"[red]Request timed out or was canceled:[/] {Markup.Escape(ex.Message)}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected error:[/] {Markup.Escape(ex.Message)}");
        }
    }

    private static MemberSearchRequest BuildRequest(string selectedSearchType)
    {
        var requestHeader = new NfcuRequestHeader
        {
            RqUID = Guid.NewGuid().ToString(),
            Credential = "aps-exchange-user",
            ConsumerChannel = "HBK",
            ConsumingApplicationName = "DEMO"
        };

        return selectedSearchType switch
        {
            "SSN" => new MemberSearchRequest
            {
                NFCURequestHeader = requestHeader,
                Ssn = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter approved test [green]SSN digits only[/]:")
                        .Secret()),
                MembershipType = "P"
            },

            "Phone number" => new MemberSearchRequest
            {
                NFCURequestHeader = requestHeader,
                PhoneNumber = AnsiConsole.Ask<string>("Enter approved test [green]phone number digits only[/]:"),
                MembershipType = "P"
            },

            _ => new MemberSearchRequest
            {
                NFCURequestHeader = requestHeader,
                FirstName = AnsiConsole.Ask<string>("Enter [green]first name[/]:"),
                LastName = AnsiConsole.Ask<string>("Enter [green]last name[/]:"),
                MembershipType = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select [green]membership type[/]:")
                        .AddChoices("P", "C"))
            }
        };
    }
}
