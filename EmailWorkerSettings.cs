namespace EmailWorkerService
{
    public class EmailWorkerSettings
    {
 
        public AuthSettings Auth { get; set; } = new AuthSettings();
        public SmtpSettings Smtp { get; set; } = new SmtpSettings();
        public string DefaultSenderEmail { get; set; } = "support@victoriabraidsandweaves.org";
        public int CheckIntervalSeconds { get; set; } = 60;
    }

    public class AuthSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string TokenEndpoint { get; set; } = "/api/auth/login"; 
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public string Notificationendpoint { get; set; } = string.Empty;
        public string Acknowledgementendpoint { get; set; } = string.Empty;
        public string Successendpoint { get; set; } = string.Empty;
    }

   
    public class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 465; // Default STARTTLS port
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool EnableSsl { get; set; } = true;
    }
}