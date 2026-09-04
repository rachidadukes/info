using System.Net.Http.Json;
using LegalHoldAdmin.Models;

namespace LegalHoldAdmin.Services;

public sealed class MemberApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public MemberApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<ResultWrapper<string>> SearchMemberAsync(string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "member/v1/members/search");

            request.Headers.Add("x-nfcu-clientid", GetRequiredSetting("MemberApi:ClientId"));
            request.Headers.Add("x-nfcu-clientsecret", GetRequiredSetting("MemberApi:ClientSecret"));

            var memberSearchRequest = new MemberSearchRequest
            {
                NFCURequestHeader = new NfcuRequestHeader
                {
                    Credential = GetRequiredSetting("MemberApi:Credential"),
                    RqUID = Guid.NewGuid().ToString(),
                    ConsumerChannel = GetRequiredSetting("MemberApi:ConsumerChannel"),
                    ConsumingApplicationName = GetRequiredSetting("MemberApi:ConsumingApplicationName")
                },
                MembershipType = "P",
                FirstName = firstName,
                LastName = lastName,
                ActiveStatusIndicator = "0"
            };

            request.Content = JsonContent.Create(memberSearchRequest);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ResultWrapper<string>.Failure(
                    $"HTTP Error: {(int)response.StatusCode} {response.ReasonPhrase}",
                    responseBody);
            }

            return ResultWrapper<string>.Success(responseBody);
        }
        catch (InvalidOperationException ex)
        {
            return ResultWrapper<string>.Failure($"Configuration Error: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return ResultWrapper<string>.Failure($"HTTP Error: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultWrapper<string>.Failure($"Unexpected Error: {ex.Message}");
        }
    }

    private string GetRequiredSetting(string key)
    {
        return _configuration[key]
            ?? throw new InvalidOperationException($"Missing required configuration setting '{key}'.");
    }
}
