using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace WpfApiTester;

public partial class MainWindow : Window
{
    private bool Legacy;

    private readonly string _databasePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\Requests.sqlite"));

    private readonly ObservableCollection<RequestListItem>      _requests         = [];
    private readonly ObservableCollection<UrlItem>             _urls             = [];
    private readonly ObservableCollection<TransactionTypeItem> _transactionTypes = [];

  

    public MainWindow()
    {
        InitializeComponent();
        RequestsListBox.ItemsSource      = _requests;
        UrlListBox.ItemsSource           = _urls;
        TransactionTypeComboBox.ItemsSource = _transactionTypes;
        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadTransactionTypesAsync();
        await HistoryControl.InitializeAsync(_databasePath);
    }

    private async Task LoadUrlsAsync(string urlType)
    {
        _urls.Clear();
        if (!File.Exists(_databasePath)) return;

        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();

            const string sql = "SELECT ID, Type, Endpoint, Environment FROM URL WHERE Type = @type ORDER BY ID";
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@type", urlType);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                _urls.Add(new UrlItem(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
            // No auto-select — user must choose a URL to trigger request loading
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Warning: could not load URLs — {ex.Message}";
        }
    }

    private async void TransactionTypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Clear dependent controls first
        _requests.Clear();
        ClearRequestDisplay();

        if (TransactionTypeComboBox.SelectedItem is not TransactionTypeItem selected) return;

        // Map TransactionType → URL.Type
        var urlType = selected.Type switch
        {
            TransactionType.MemberSearch  => TransactionType.MemberSearch,
            TransactionType.MemberProfile => TransactionType.MemberProfile,
            _                             => "Transaction"   // CD_Deposit, IRA_CD_Deposit → Transaction
        };

        await LoadUrlsAsync(urlType);
    }

    private async void UrlListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (UrlListBox.SelectedItem is not UrlItem selectedUrl)
        {
            // Nothing selected — keep lists empty
            _requests.Clear();
            ClearRequestDisplay();
            return;
        }

        var type = selectedUrl.Type;

        Legacy = string.Equals(selectedUrl.Environment, "Legacy", StringComparison.OrdinalIgnoreCase);

        CDdepositButton.IsEnabled    = string.Equals(type, TransactionType.CD_Deposit,    StringComparison.OrdinalIgnoreCase);
        MemberSearchButton.IsEnabled = string.Equals(type, TransactionType.MemberSearch,  StringComparison.OrdinalIgnoreCase);
        MemberInfoButton.IsEnabled   = string.Equals(type, TransactionType.MemberProfile, StringComparison.OrdinalIgnoreCase);

        await LoadRequestsAsync(type);
    }



    private async Task LoadRequestsAsync(string? type)
    {
        _requests.Clear();
        ClearRequestDisplay();
        ResponseTextBox.Clear();
        StatusTextBlock.Text = "Loading requests...";

        if (!File.Exists(_databasePath))
        {
            StatusTextBlock.Text = "Requests.sqlite was not found.";
            return;
        }

        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();

            var sql = string.IsNullOrWhiteSpace(type)
                ? "SELECT ID, Description, Type, Request FROM Requests ORDER BY ID"
                : "SELECT ID, Description, Type, Request FROM Requests WHERE Type = @type ORDER BY ID";

            await using var command = new SqliteCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(type))
                command.Parameters.AddWithValue("@type", type);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                _requests.Add(new RequestListItem(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }

            StatusTextBlock.Text = $"Loaded {_requests.Count} request(s).";

            if (_requests.Count > 0)
                RequestsListBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Failed to load requests.";
            MessageBox.Show(ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RequestsListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RequestsListBox.SelectedItem is not RequestListItem selectedItem)
        {
            ClearRequestDisplay();
            return;
        }

        DisplayXml(FormatXmlIfPossible(selectedItem.RequestBody));
    }

    // ── RichTextBox helpers ──────────────────────────────────────────────────

    // Matches a line like:   <CustPermId>19537</CustPermId>
    // Group 1 = "<CustPermId>"  Group 2 = "19537"  Group 3 = "</CustPermId>"
    private static readonly Regex LeafElementRegex = new(
        @"^(\s*<[A-Za-z][^>]*>)([^<]+)(<\/[A-Za-z][^>]*>)\s*$",
        RegexOptions.Compiled);

    // Element names whose tags are always rendered blue and bold
    private static readonly HashSet<string> KeyElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustPermId",
    "TranCode",
    "AcctId",
    "AcctType",
    "FirstName",
    "LastName"
    };

    private void ClearRequestDisplay()
    {
      
        SelectedRequestRichTextBox.Document.Blocks.Clear();
    }

    private void DisplayXml(string xml)
    {
        SelectedRequestRichTextBox.Document.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(xml)) return;

        var blue = new SolidColorBrush(Color.FromRgb(180, 0, 0));
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 1 };
        var lines = xml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = LeafElementRegex.Match(line);

            if (match.Success)
            {
                var nameMatch = Regex.Match(match.Groups[1].Value.TrimStart(), @"<([A-Za-z][A-Za-z0-9_]*)");
                var elementName = nameMatch.Success ? nameMatch.Groups[1].Value : string.Empty;
                var isBlue = KeyElementNames.Contains(elementName);

                paragraph.Inlines.Add(new Run(match.Groups[1].Value)   // opening tag
                {
                    FontWeight = isBlue ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isBlue ? blue : Brushes.Black
                });
                paragraph.Inlines.Add(new Run(match.Groups[2].Value)   // value — always bold black
                {
                    FontWeight = FontWeights.Bold
                });
                paragraph.Inlines.Add(new Run(match.Groups[3].Value)   // closing tag
                {
                    FontWeight = isBlue ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isBlue ? blue : Brushes.Black
                });
            }
            else
            {
                paragraph.Inlines.Add(new Run(line));
            }

            if (i < lines.Length - 1)
                paragraph.Inlines.Add(new LineBreak());
        }

        SelectedRequestRichTextBox.Document.Blocks.Add(paragraph);
    }

    private static string GetRichTextBoxText(System.Windows.Controls.RichTextBox rtb) =>
        new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text.TrimEnd();

    // ── API call ─────────────────────────────────────────────────────────────

    private async void MemberSearchButton_OnClick(object sender, RoutedEventArgs e) => await CallMemberSearchAsync();
    private async void MemberInfoButton_OnClick(object sender, RoutedEventArgs e)   => await CallMemberProfileAsync();
    private async void CDdepositButton_OnClick(object sender, RoutedEventArgs e)         => await CDdepositAPIAsync();

    private async Task CallMemberSearchAsync()
    {
        var requestBody = GetRichTextBoxText(SelectedRequestRichTextBox);

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            MessageBox.Show("Select a request before calling the API.", "No Request Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (UrlListBox.SelectedItem is not UrlItem selectedUrl || string.IsNullOrWhiteSpace(selectedUrl.Endpoint))
        {
            MessageBox.Show("Select an endpoint before calling the API.", "No Endpoint Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var description = (RequestsListBox.SelectedItem as RequestListItem)?.Description ?? string.Empty;

        MemberSearchButton.IsEnabled = false;
        StatusTextBlock.Text = "Sending Member Search request...";

        try
        {
            var memberSearch = new MemberSearch();
            var responseBody = await memberSearch.GetResponseAsync(requestBody, selectedUrl.Endpoint,  Legacy);

            StatusTextBlock.Text = "Member Search completed.";
            ResponseTextBox.Text = FormatXmlIfPossible(responseBody);
            ResponseTab.IsSelected = true;

            var entry = new ApiCallHistory(DateTime.Now, description, requestBody, responseBody, null, TransactionType.MemberSearch, !string.IsNullOrWhiteSpace(responseBody));
            await SaveResponseAsync(entry);

            MessageBox.Show("Member Search completed. Check the Response tab for details.", "Member Search Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Member Search failed.";
            ResponseTextBox.Text = ex.ToString();
            ResponseTab.IsSelected = true;

            MessageBox.Show("Member Search failed. Check the Response tab for details.", "Member Search Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            MemberSearchButton.IsEnabled = true;
        }
    }

    private async Task CallMemberProfileAsync()
    {
        var requestBody = GetRichTextBoxText(SelectedRequestRichTextBox);

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            MessageBox.Show("Select a request before calling the API.", "No Request Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (UrlListBox.SelectedItem is not UrlItem selectedUrl || string.IsNullOrWhiteSpace(selectedUrl.Endpoint))
        {
            MessageBox.Show("Select an endpoint before calling the API.", "No Endpoint Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var description = (RequestsListBox.SelectedItem as RequestListItem)?.Description ?? string.Empty;

        MemberSearchButton.IsEnabled = false;
        StatusTextBlock.Text = "Sending Member Search request...";

        try
        {
            var memberProfile = new MemberProfile();
            var responseBody = await memberProfile.GetResponseAsync(requestBody, selectedUrl.Endpoint, Legacy);

            StatusTextBlock.Text = "Member Search completed.";
            ResponseTextBox.Text = FormatXmlIfPossible(responseBody);
            ResponseTab.IsSelected = true;

            var entry = new ApiCallHistory(DateTime.Now, description, requestBody, responseBody, null, TransactionType.MemberProfile, !string.IsNullOrWhiteSpace(responseBody));
            await SaveResponseAsync(entry);

            MessageBox.Show("Member Profile completed. Check the Response tab for details.", "Member Profile Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Member Profile failed.";
            ResponseTextBox.Text = ex.ToString();
            ResponseTab.IsSelected = true;

            MessageBox.Show("Member Profile failed. Check the Response tab for details.", "Member Profile Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            MemberSearchButton.IsEnabled = true;
        }
    }

    private async Task CDdepositAPIAsync()
    {
        var requestBody = GetRichTextBoxText(SelectedRequestRichTextBox);

        if (string.IsNullOrWhiteSpace(requestBody))
        {
            MessageBox.Show("Select a request before calling the API.", "No Request Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (UrlListBox.SelectedItem is not UrlItem selectedUrl || string.IsNullOrWhiteSpace(selectedUrl.Endpoint))
        {
            MessageBox.Show("Select an endpoint before calling the API.", "No Endpoint Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var description = (RequestsListBox.SelectedItem as RequestListItem)?.Description ?? string.Empty;

        CDdepositButton.IsEnabled    = false;
        MemberSearchButton.IsEnabled = false;
        MemberInfoButton.IsEnabled   = false;
        StatusTextBlock.Text = "Sending CD Deposit request...";

        try
        {
            var cdDeposit = new CDdeposit();
            var responseBody = await cdDeposit.GetResponseAsync(requestBody, selectedUrl.Endpoint, Legacy);

            StatusTextBlock.Text = "CD Deposit completed.";
            ResponseTextBox.Text = FormatXmlIfPossible(responseBody);
            ResponseTab.IsSelected = true;

            var entry = new ApiCallHistory(DateTime.Now, description, requestBody, responseBody, null, TransactionType.CD_Deposit, !string.IsNullOrWhiteSpace(responseBody));
            await SaveResponseAsync(entry);

            MessageBox.Show("CD Deposit completed. Check the Response tab for details.", "CD Deposit Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "CD Deposit failed.";
            ResponseTextBox.Text = ex.ToString();
            ResponseTab.IsSelected = true;

            MessageBox.Show("CD Deposit failed. Check the Response tab for details.", "CD Deposit Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            var type = (UrlListBox.SelectedItem as UrlItem)?.Type ?? string.Empty;
            CDdepositButton.IsEnabled    = string.Equals(type, TransactionType.CD_Deposit,     StringComparison.OrdinalIgnoreCase);
            MemberSearchButton.IsEnabled = string.Equals(type, TransactionType.MemberSearch, StringComparison.OrdinalIgnoreCase);
            MemberInfoButton.IsEnabled   = string.Equals(type, TransactionType.MemberProfile,   StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task SaveResponseAsync(ApiCallHistory entry)
    {
        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();

            const string sql = """
                INSERT INTO Responses (Description, Request, Response, DateStamp, StatusCode, StatusPhrase)
                VALUES (@description, @request, @response, @dateStamp, @statusCode, @statusPhrase)
                """;

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@description",  (object?)entry.Description  ?? DBNull.Value);
            command.Parameters.AddWithValue("@request",      (object?)entry.RequestBody   ?? DBNull.Value);
            command.Parameters.AddWithValue("@response",     (object?)entry.ResponseBody  ?? DBNull.Value);
            command.Parameters.AddWithValue("@dateStamp",    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@statusCode",   (object?)entry.StatusCode    ?? DBNull.Value);
            command.Parameters.AddWithValue("@statusPhrase", (object?)entry.StatusPhrase  ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Warning: could not save to Responses table — {ex.Message}";
        }
    }

    private static string FormatXmlIfPossible(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return responseText;

        try
        {
            using var stringReader = new StringReader(responseText.Trim());
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings { IgnoreWhitespace = true });
            var document = XDocument.Load(xmlReader, LoadOptions.None);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = document.Declaration is null
            };
            using var stringWriter = new StringWriter();
            using var xmlWriter = XmlWriter.Create(stringWriter, settings);
            document.Save(xmlWriter);
            xmlWriter.Flush();
            return stringWriter.ToString();
        }
        catch
        {
            return responseText;
        }
    }

    private async Task LoadTransactionTypesAsync()
    {
        _transactionTypes.Clear();
        if (!File.Exists(_databasePath)) return;

        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync();

            const string sql = "SELECT Type, Description FROM TransactionType ORDER BY ID";
            await using var command = new SqliteCommand(sql, connection);
            await using var reader  = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                _transactionTypes.Add(new TransactionTypeItem(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Warning: could not load transaction types — {ex.Message}";
        }
    }

    private sealed record RequestListItem(int Id, string Description, string Type, string RequestBody)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(Description)
            ? $"Request {Id}"
            : $"{Id} - {Description}";
    }

    private sealed record UrlItem(int Id, string Type, string Endpoint, string Environment);

    private sealed record TransactionTypeItem(string Type, string Description)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(Description)
            ? Type
            : $"{Type} — {Description}";
    }
}

public sealed record ApiCallHistory(
    DateTime Timestamp,
    string Description,
    string RequestBody,
    string ResponseBody,
    int? StatusCode,
    string? StatusPhrase,
    bool IsSuccess)
{
    public string DisplayText =>
        $"{Timestamp:HH:mm:ss}  {(StatusCode.HasValue ? $"{StatusCode} {StatusPhrase}" : StatusPhrase)}  {(string.IsNullOrWhiteSpace(Description) ? "(no description)" : Description)}";

    public string StatusBadge => IsSuccess ? "OK" : "FAIL";
}
