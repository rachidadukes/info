namespace MyApp.Domain.Entities;

public class HistoryGroupModel
{
    public string?   Description { get; set; }
    public string?   Environment { get; set; }
    public int       Count       { get; set; }
    public DateTime  LastRun     { get; set; }
}
