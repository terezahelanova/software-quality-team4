namespace StocksReportingLibrary.Configuration;
public class EmailSettings
{
    public const string Path = "EmailSettings";
    public required string SmtpServer { get; set; }
    public int Port { get; set; } = 587;
    public required string SenderEmail { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}
