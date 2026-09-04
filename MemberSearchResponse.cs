using System.Text.Json.Serialization;

namespace LegalHoldAdmin.Models;

public sealed class MemberSearchResponse
{
    [JsonPropertyName("members")]
    public List<MemberSearchMember> Members { get; init; } = new();
}

public sealed class MemberSearchMember
{
    [JsonPropertyName("accessNumber")]
    public string? AccessNumber { get; init; }

    [JsonPropertyName("membershipStatus")]
    public string? MembershipStatus { get; init; }

    [JsonPropertyName("membershipType")]
    public string? MembershipType { get; init; }

    [JsonPropertyName("dateOfBirth")]
    public string? DateOfBirth { get; init; }

    [JsonPropertyName("personData")]
    public MemberPersonData? PersonData { get; init; }

    [JsonPropertyName("emailAddresses")]
    public List<MemberEmailAddress> EmailAddresses { get; init; } = new();

    [JsonPropertyName("phoneNumbers")]
    public List<MemberPhoneNumber> PhoneNumbers { get; init; } = new();

    [JsonIgnore]
    public string DisplayName => PersonData?.FullName ?? string.Empty;

    [JsonIgnore]
    public string PrimaryEmail => EmailAddresses.FirstOrDefault()?.EmailAddress ?? string.Empty;

    [JsonIgnore]
    public string PrimaryPhone => PhoneNumbers.FirstOrDefault()?.PhoneNumber ?? string.Empty;
}

public sealed class MemberPersonData
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; init; }
}

public sealed class MemberEmailAddress
{
    [JsonPropertyName("emailAddress")]
    public string? EmailAddress { get; init; }
}

public sealed class MemberPhoneNumber
{
    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; init; }
}
