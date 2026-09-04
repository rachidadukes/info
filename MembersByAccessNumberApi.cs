using System.Net.Http.Json;
using LegalHoldAdmin.Models;

namespace LegalHoldAdmin.Services;

public sealed class MembersByAccessNumberApi
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public MembersByAccessNumberApi(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<ResultWrapper<string>> SearchMemberAsync(string accessNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpointPath = GetRequiredSetting("MemberAccessNumberApi:EndpointPath");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointPath);

            request.Headers.Add("x-nfcu-clientid", GetRequiredSetting("MemberApi:ClientId"));
            request.Headers.Add("x-nfcu-clientsecret", GetRequiredSetting("MemberApi:ClientSecret"));

            var memberAccessNumberRequest = new MemberAccessNumberRequest
            {
                NECUREquestHeader = new NecuRequestHeader
                {
                    RqUID = Guid.NewGuid().ToString(),
                    ConsumerChannel = GetRequiredSetting("MemberAccessNumberApi:ConsumerChannel"),
                    ConsumingApplicationName = GetRequiredSetting("MemberAccessNumberApi:ConsumingApplicationName"),
                    Credential = GetRequiredSetting("MemberAccessNumberApi:Credential")
                },
                AccessNumber = accessNumber
            };

            request.Content = JsonContent.Create(memberAccessNumberRequest);

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
