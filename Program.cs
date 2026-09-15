using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Security.ActiveDirectory;
using Security.Core.Options;
using Spectre.Console;

var parsedArgs = ParsedArgs.From(args);
var settings = AppSettings.Load(parsedArgs.ConfigPath);
var client = new ActiveDirectoryClient(settings.ActiveDirectory);

try
{
    if (parsedArgs.Command is null)
    {
        await RunMenuAsync(settings, client);
        return;
    }

    await RunCommandAsync(parsedArgs, settings, client);
}
catch (Exception ex) when (ex is LdapException or DirectoryOperationException or InvalidOperationException)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
}

static async Task RunMenuAsync(AppSettings settings, ActiveDirectoryClient client)
{
    while (true)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Security Directory Tool")
                .AddChoices(
                    "Show configured role mappings",
                    "List users for a role",
                    "Search AD groups",
                    "Show current Windows identity",
                    "Exit"));

        switch (action)
        {
            case "Show configured role mappings":
                ShowConfiguredGroups(settings.SecurityAuthorization.GroupMappings);
                break;
            case "List users for a role":
                var role = PromptForRole(settings);
                await ShowUsersForRoleAsync(settings, client, role);
                break;
            case "Search AD groups":
                var term = AnsiConsole.Ask<string>("Search group name:");
                await SearchGroupsAsync(client, term);
                break;
            case "Show current Windows identity":
                ShowCurrentIdentity();
                break;
            case "Exit":
                return;
        }
    }
}

static async Task RunCommandAsync(ParsedArgs parsedArgs, AppSettings settings, ActiveDirectoryClient client)
{
    switch (parsedArgs.Command)
    {
        case "groups":
            ShowConfiguredGroups(settings.SecurityAuthorization.GroupMappings);
            break;
        case "users":
            var role = parsedArgs.Values.FirstOrDefault()
                ?? throw new InvalidOperationException("Usage: users <role>");
            await ShowUsersForRoleAsync(settings, client, role);
            break;
        case "search-groups":
            var term = parsedArgs.Values.FirstOrDefault()
                ?? throw new InvalidOperationException("Usage: search-groups <term>");
            await SearchGroupsAsync(client, term);
            break;
        case "whoami":
            ShowCurrentIdentity();
            break;
        default:
            ShowHelp();
            break;
    }
}

static string PromptForRole(AppSettings settings)
{
    var roles = settings.SecurityAuthorization.GroupMappings
        .OrderBy(mapping => mapping.Precedence)
        .Select(mapping => mapping.Role)
        .Where(role => !string.IsNullOrWhiteSpace(role))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (roles.Length == 0)
    {
        throw new InvalidOperationException("No roles are configured in SecurityAuthorization:GroupMappings.");
    }

    return AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select role")
            .AddChoices(roles));
}

static void ShowConfiguredGroups(IReadOnlyList<SecurityGroupMapping> mappings)
{
    var table = new Table()
        .Title("Configured Role-to-AD Group Mappings")
        .AddColumn("Role")
        .AddColumn("AD Group")
        .AddColumn("Distinguished Name")
        .AddColumn("Authority")
        .AddColumn("Precedence");

    foreach (var mapping in mappings.OrderBy(mapping => mapping.Precedence))
    {
        table.AddRow(
            Markup.Escape(mapping.Role),
            Markup.Escape(mapping.GroupName),
            Markup.Escape(mapping.DistinguishedName),
            mapping.AuthorityLevel.ToString(),
            mapping.Precedence.ToString());
    }

    AnsiConsole.Write(table);
}

static async Task ShowUsersForRoleAsync(
    AppSettings settings,
    ActiveDirectoryClient client,
    string role)
{
    var mapping = settings.SecurityAuthorization.GroupMappings
        .OrderBy(mapping => mapping.Precedence)
        .FirstOrDefault(mapping => string.Equals(mapping.Role, role, StringComparison.OrdinalIgnoreCase));

    if (mapping is null)
    {
        throw new InvalidOperationException($"Role '{role}' is not configured.");
    }

    if (HasPlaceholderGroupSettings(mapping))
    {
        AnsiConsole.MarkupLine(
            $"[yellow]Role '{Markup.Escape(role)}' still has placeholder AD group settings.[/]");
        var searchTerm = AnsiConsole.Ask(
            $"Search AD groups for role '{Markup.Escape(role)}':",
            role);
        var selectedGroup = await SearchAndSelectGroupAsync(client, searchTerm);

        if (selectedGroup is null)
        {
            return;
        }

        await ShowUsersForGroupAsync(client, selectedGroup, role);
        return;
    }

    var group = await ResolveGroupAsync(client, mapping);
    await ShowUsersForGroupAsync(client, group, mapping.Role);
}

static async Task SearchGroupsAsync(ActiveDirectoryClient client, string term)
{
    var selectedGroup = await SearchAndSelectGroupAsync(client, term);

    if (selectedGroup is null)
    {
        return;
    }

    await ShowUsersForGroupAsync(client, selectedGroup, selectedGroup.Name);
}

static async Task<DirectoryGroup?> SearchAndSelectGroupAsync(
    ActiveDirectoryClient client,
    string term)
{
    var groups = await client.SearchGroupsAsync(term);
    var table = new Table()
        .Title($"AD Groups Matching '{Markup.Escape(term)}'")
        .AddColumn("Name")
        .AddColumn("SAM Account")
        .AddColumn("Distinguished Name");

    foreach (var group in groups)
    {
        table.AddRow(
            Markup.Escape(group.Name),
            Markup.Escape(group.SamAccountName),
            Markup.Escape(group.DistinguishedName));
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"[grey]{groups.Count} group{(groups.Count == 1 ? string.Empty : "s")} found.[/]");

    if (groups.Count == 0)
    {
        return null;
    }

    var choices = groups
        .Select(group => new GroupSelection(group))
        .Append(GroupSelection.Cancel)
        .ToArray();

    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<GroupSelection>()
            .Title("Select a group to list its users")
            .UseConverter(selection => selection.DisplayName)
            .AddChoices(choices));

    if (selected.Group is null)
    {
        return null;
    }

    return selected.Group;
}

static async Task ShowUsersForGroupAsync(
    ActiveDirectoryClient client,
    DirectoryGroup group,
    string titleContext)
{
    var users = await client.GetUsersInGroupAsync(group.DistinguishedName);

    AnsiConsole.Write(
        new Panel(
            new Rows(
                new Markup($"[bold]Selected role/group:[/] {Markup.Escape(titleContext)}"),
                new Markup($"[bold]AD group:[/] {Markup.Escape(group.Name)}"),
                new Markup($"[bold]Distinguished name:[/] {Markup.Escape(group.DistinguishedName)}"),
                new Markup($"[bold]Users found:[/] {users.Count}")))
        .Header("Selected Directory Group"));

    ShowUsersPaged(group, titleContext, users);
}

static void ShowUsersPaged(
    DirectoryGroup group,
    string titleContext,
    IReadOnlyList<DirectoryUser> users)
{
    const int pageSize = 15;
    var pageIndex = 0;
    var pageCount = Math.Max(1, (int)Math.Ceiling(users.Count / (double)pageSize));

    while (true)
    {
        var pageUsers = users
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToArray();

        var table = new Table()
        .Title($"Users In {group.Name} ({titleContext}) - Page {pageIndex + 1} of {pageCount}")
        .AddColumn("Employee ID")
        .AddColumn("Display Name")
        .AddColumn("User Principal Name")
        .AddColumn("Distinguished Name");

        foreach (var user in pageUsers)
        {
            table.AddRow(
                Markup.Escape(user.EmployeeId),
                Markup.Escape(user.DisplayName),
                Markup.Escape(user.UserPrincipalName),
                Markup.Escape(user.DistinguishedName));
        }

        AnsiConsole.Write(table);

        if (pageCount == 1)
        {
            AnsiConsole.MarkupLine($"[grey]{users.Count} user{(users.Count == 1 ? string.Empty : "s")} found.[/]");
            return;
        }

        var choices = new List<string>();
        if (pageIndex < pageCount - 1)
        {
            choices.Add("Next page");
        }

        if (pageIndex > 0)
        {
            choices.Add("Previous page");
        }

        choices.Add("Back to menu");

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Showing {pageUsers.Length} of {users.Count} users")
                .AddChoices(choices));

        switch (action)
        {
            case "Next page":
                pageIndex++;
                break;
            case "Previous page":
                pageIndex--;
                break;
            default:
                return;
        }
    }
}

static async Task<DirectoryGroup> ResolveGroupAsync(
    ActiveDirectoryClient client,
    SecurityGroupMapping mapping)
{
    if (!string.IsNullOrWhiteSpace(mapping.DistinguishedName) &&
        !mapping.DistinguishedName.Contains("TODO_", StringComparison.OrdinalIgnoreCase))
    {
        return new DirectoryGroup(mapping.GroupName, mapping.GroupName, mapping.DistinguishedName);
    }

    if (string.IsNullOrWhiteSpace(mapping.GroupName) ||
        mapping.GroupName.Contains("TODO_", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Role '{mapping.Role}' still has placeholder AD group settings. Search for the real group, then update SecurityAuthorization:GroupMappings.");
    }

    var groups = await client.FindGroupByNameAsync(mapping.GroupName);
    return groups.Count switch
    {
        0 => throw new InvalidOperationException($"AD group '{mapping.GroupName}' was not found."),
        1 => groups[0],
        _ => throw new InvalidOperationException(
            $"AD group '{mapping.GroupName}' matched more than one group. Configure the DistinguishedName to make it exact.")
    };
}

static bool HasPlaceholderGroupSettings(SecurityGroupMapping mapping)
{
    return string.IsNullOrWhiteSpace(mapping.GroupName) ||
        string.IsNullOrWhiteSpace(mapping.DistinguishedName) ||
        mapping.GroupName.Contains("TODO_", StringComparison.OrdinalIgnoreCase) ||
        mapping.DistinguishedName.Contains("TODO_", StringComparison.OrdinalIgnoreCase);
}

static void ShowCurrentIdentity()
{
    var identity = WindowsIdentity.GetCurrent();
    var table = new Table()
        .Title("Current Windows Identity")
        .AddColumn("Name")
        .AddColumn("Authentication Type")
        .AddColumn("Authenticated");

    table.AddRow(
        Markup.Escape(identity.Name),
        Markup.Escape(identity.AuthenticationType ?? string.Empty),
        identity.IsAuthenticated ? "Yes" : "No");

    AnsiConsole.Write(table);
}

static void ShowHelp()
{
    AnsiConsole.Write(
        new Panel(
            """
            Usage:
              Security.DirectoryTool [--config <path>] groups
              Security.DirectoryTool [--config <path>] users <role>
              Security.DirectoryTool [--config <path>] search-groups <term>
              Security.DirectoryTool whoami

            Run with no command to open the interactive menu.
            """)
        .Header("Security Directory Tool"));
}

public sealed class AppSettings
{
    public ActiveDirectoryOptions ActiveDirectory { get; init; } = new();

    public SecurityAuthorizationOptions SecurityAuthorization { get; init; } = new();

    public static AppSettings Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new InvalidOperationException($"Config file was not found: {configPath}");
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var settings = JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(configPath),
            jsonOptions);

        return settings ?? throw new InvalidOperationException($"Config file is empty: {configPath}");
    }
}

public sealed class ActiveDirectoryClient(ActiveDirectoryOptions options)
{
    public Task<IReadOnlyList<DirectoryGroup>> SearchGroupsAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            throw new InvalidOperationException("Search term is required.");
        }

        var escapedTerm = EscapeLdapFilterValue(term.Trim());
        var filter = $"(&(objectClass=group)(|(cn=*{escapedTerm}*)(name=*{escapedTerm}*)(sAMAccountName=*{escapedTerm}*)))";

        return SearchGroupsWithFilterAsync(filter);
    }

    public Task<IReadOnlyList<DirectoryGroup>> FindGroupByNameAsync(string groupName)
    {
        var escapedGroupName = EscapeLdapFilterValue(groupName.Trim());
        var filter = $"(&(objectClass=group)(|(cn={escapedGroupName})(name={escapedGroupName})(sAMAccountName={escapedGroupName})))";

        return SearchGroupsWithFilterAsync(filter);
    }

    public Task<IReadOnlyList<DirectoryUser>> GetUsersInGroupAsync(string groupDistinguishedName)
    {
        if (string.IsNullOrWhiteSpace(groupDistinguishedName))
        {
            throw new InvalidOperationException("Group distinguished name is required.");
        }

        var escapedDn = EscapeLdapFilterValue(groupDistinguishedName);
        var filter = $"(&(objectCategory=person)(objectClass=user)(memberOf={escapedDn}))";

        return SearchUsersWithFilterAsync(filter);
    }

    private async Task<IReadOnlyList<DirectoryGroup>> SearchGroupsWithFilterAsync(string filter)
    {
        using var connection = CreateConnection();
        var results = await SearchPagedAsync(
            connection,
            filter,
            "cn",
            "name",
            "sAMAccountName",
            "distinguishedName");

        return results
            .Select(entry => new DirectoryGroup(
                ReadSingleAttribute(entry, "name") ?? ReadSingleAttribute(entry, "cn") ?? string.Empty,
                ReadSingleAttribute(entry, "sAMAccountName") ?? string.Empty,
                ReadSingleAttribute(entry, "distinguishedName") ?? string.Empty))
            .Where(group => !string.IsNullOrWhiteSpace(group.DistinguishedName))
            .OrderBy(group => group.Name)
            .ToArray();
    }

    private async Task<IReadOnlyList<DirectoryUser>> SearchUsersWithFilterAsync(string filter)
    {
        using var connection = CreateConnection();
        var results = await SearchPagedAsync(
            connection,
            filter,
            options.EmployeeIdAttribute,
            options.DisplayNameAttribute,
            "userPrincipalName",
            "distinguishedName");

        return results
            .Select(entry => new DirectoryUser(
                ReadSingleAttribute(entry, options.EmployeeIdAttribute) ?? string.Empty,
                ReadSingleAttribute(entry, options.DisplayNameAttribute) ?? string.Empty,
                ReadSingleAttribute(entry, "userPrincipalName") ?? string.Empty,
                ReadSingleAttribute(entry, "distinguishedName") ?? string.Empty))
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.EmployeeId)
            .ToArray();
    }

    private LdapConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(options.Server) ||
            string.IsNullOrWhiteSpace(options.SearchBaseDn))
        {
            throw new InvalidOperationException("ActiveDirectory:Server and ActiveDirectory:SearchBaseDn are required.");
        }

        var connection = new LdapConnection(new LdapDirectoryIdentifier(options.Server, options.Port))
        {
            AuthType = options.AuthType,
            Credential = CredentialCache.DefaultNetworkCredentials
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = options.UseSsl;
        connection.Bind();

        return connection;
    }

    private async Task<IReadOnlyList<SearchResultEntry>> SearchPagedAsync(
        LdapConnection connection,
        string filter,
        params string[] attributes)
    {
        var entries = new List<SearchResultEntry>();
        var cookie = Array.Empty<byte>();

        do
        {
            var request = new SearchRequest(
                options.SearchBaseDn,
                filter,
                SearchScope.Subtree,
                attributes);
            request.Controls.Add(new PageResultRequestControl(500) { Cookie = cookie });

            var response = await Task.Run(() => (SearchResponse)connection.SendRequest(request));
            entries.AddRange(response.Entries.Cast<SearchResultEntry>());

            var pageResponse = response.Controls
                .OfType<PageResultResponseControl>()
                .FirstOrDefault();
            cookie = pageResponse?.Cookie ?? [];
        }
        while (cookie.Length > 0);

        return entries;
    }

    private static string? ReadSingleAttribute(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName) || entry.Attributes[attributeName].Count == 0)
        {
            return null;
        }

        return entry.Attributes[attributeName]
            .GetValues(typeof(string))
            .Cast<string>()
            .FirstOrDefault();
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace(@"\", @"\5c", StringComparison.Ordinal)
            .Replace("*", @"\2a", StringComparison.Ordinal)
            .Replace("(", @"\28", StringComparison.Ordinal)
            .Replace(")", @"\29", StringComparison.Ordinal)
            .Replace("\0", @"\00", StringComparison.Ordinal);
    }
}

public sealed record DirectoryGroup(
    string Name,
    string SamAccountName,
    string DistinguishedName);

public sealed record GroupSelection(DirectoryGroup? Group)
{
    public static GroupSelection Cancel { get; } = new((DirectoryGroup?)null);

    public string DisplayName =>
        Group is null
            ? "Back to menu"
            : $"{Group.Name} | {Group.SamAccountName}";
}

public sealed record DirectoryUser(
    string EmployeeId,
    string DisplayName,
    string UserPrincipalName,
    string DistinguishedName);

public sealed class ParsedArgs
{
    public string ConfigPath { get; private init; } = "appsettings.json";

    public string? Command { get; private init; }

    public IReadOnlyList<string> Values { get; private init; } = [];

    public static ParsedArgs From(string[] args)
    {
        var remaining = new List<string>();
        var configPath = "appsettings.json";

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--config", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException("--config requires a path.");
                }

                configPath = args[++index];
            }
            else
            {
                remaining.Add(args[index]);
            }
        }

        return new ParsedArgs
        {
            ConfigPath = configPath,
            Command = remaining.FirstOrDefault()?.ToLowerInvariant(),
            Values = remaining.Skip(1).ToArray()
        };
    }
}
