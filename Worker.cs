using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Formats.Asn1.AsnWriter;
using System.Text.Unicode;
using System.Text;
using System.Text.RegularExpressions;

namespace EmailWorkerService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EmailWorkerSettings _settings;
        private readonly TokenService _tokenService;

        public Worker(
            ILogger<Worker> logger,
            IHttpClientFactory httpClientFactory,
            IOptions<EmailWorkerSettings> settings,
            TokenService tokenService)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _tokenService = tokenService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 🔹 Get token once per cycle
                    var token = await _tokenService.GetAccessTokenAsync(stoppingToken);

                    var notifications = await GetNotifications(token, stoppingToken);

                    if (notifications != null && notifications.Count > 0)
                    {
                        foreach (var notification in notifications)
                        {
                            try
                            {
                                await AcknowledgeNotification(notification, token, stoppingToken);

                                await SendNotificationEmail(notification);

                                await MarkNotificationSuccess(notification, token, stoppingToken);

                                _logger.LogInformation("Notification {id} processed successfully.", notification.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to process notification {id}.", notification.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing notifications.");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.CheckIntervalSeconds), stoppingToken);
            }
        }

        /// <summary>
        /// Fetches notifications from the API (GET), then filters to only those
        /// where channel is "EMAIL" and type is "BN", and the recipient is a valid email.
        /// </summary>
        private async Task<List<Notification>?> GetNotifications(string token, CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_settings.Auth.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync(_settings.Auth.Notificationendpoint, stoppingToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(stoppingToken);

            var allNotifications = JsonSerializer.Deserialize<List<Notification>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (allNotifications == null || allNotifications.Count == 0)
                return null;

            return allNotifications
                .Where(n => string.Equals(n.Channel, "EMAIL", StringComparison.OrdinalIgnoreCase))
                .Where(n => string.Equals(n.Type, "BN", StringComparison.OrdinalIgnoreCase))
                .Where(n => IsValidEmail(n.Recipient))
                .ToList();
        }

        /// <summary>
        /// Validates whether a string is a properly formatted email address.
        /// </summary>
        private static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new MailAddress(email.Trim());
                return addr.Address == email.Trim();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a single notification email using TLS on the configured SMTP port.
        /// Uses the notification's "recipient" field as the email address,
        /// "subject" as the email subject, and "content" as the email body.
        /// </summary>
        private async Task SendNotificationEmail(Notification notification)
        {
            try
            {
                using var smtpClient = new MailKit.Net.Smtp.SmtpClient();

                // Connect using SSL/TLS
                await smtpClient.ConnectAsync(
                    _settings.Smtp.Host,
                    _settings.Smtp.Port,
                    _settings.Smtp.EnableSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None
                );

                // Authenticate with SMTP credentials
                await smtpClient.AuthenticateAsync(_settings.Smtp.Username, _settings.Smtp.Password);

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Support", _settings.DefaultSenderEmail));
                message.To.Add(MailboxAddress.Parse(notification.Recipient.Trim()));
                message.Subject = notification.Subject ?? "Notification";

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = BuildEmailBody(notification);
                message.Body = bodyBuilder.ToMessageBody();

                await smtpClient.SendAsync(message);
                await smtpClient.DisconnectAsync(true);

                _logger.LogInformation("Email sent to {recipient} for notification {id}.",
                    notification.Recipient, notification.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {recipient} for notification {id}.",
                    notification.Recipient, notification.Id);
                throw; // Re-throw so the caller knows this notification failed
            }
        }

        /// <summary>
        /// Builds a formatted HTML email body from a single notification.
        /// </summary>
        private string BuildEmailBody(Notification notification)
        {
            var content = notification.Content ?? string.Empty;

            // Convert <br> and <br/> to newline
            content = Regex.Replace(content, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);

            // Remove any other HTML tags
            content = Regex.Replace(content, "<.*?>", string.Empty);

            // Encode for safety
            var encodedContent = System.Net.WebUtility.HtmlEncode(content);

            var sb = new StringBuilder();

            sb.Append("<!DOCTYPE html>");
            sb.Append("<html>");
            sb.Append("<head>");
            sb.Append("<meta charset='UTF-8'>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            sb.Append("<style>");
            sb.Append("body { font-family: Arial, sans-serif; line-height: 1.6; color: #333333; margin: 0; padding: 0; }");
            sb.Append(".container { width: 100%; max-width: 600px; margin: 0 auto; border: 1px solid #eeeeee; }");
            sb.Append(".header { background-color: #ffffff; padding: 20px; text-align: center; }");
            sb.Append(".logo { max-width: 200px; height: auto; }");
            sb.Append(".content { padding: 30px; }");
            sb.Append(".details-box { background-color: #f9f9f9; padding: 20px; border-radius: 8px; margin: 20px 0; }");
            sb.Append(".footer { background-color: #f4f4f4; padding: 20px; text-align: center; font-size: 12px; color: #777777; }");
            sb.Append(".button { background-color: #000000; color: #ffffff; padding: 12px 25px; text-decoration: none; border-radius: 5px; display: inline-block; }");
            sb.Append("</style>");
            sb.Append("</head>");
            sb.Append("<body>");
            sb.Append("<div class='container'>");

            sb.Append("<div class='header'>");
            sb.Append("<img src='https://victoriabraidsandmicrolocs.com/logo.png' alt='Victoria Braids and Weaves' class='logo'>");
            sb.Append("</div>");

            sb.Append("<div class='content'>");
            sb.Append($"<h2 style='color: #2c3e50;'>{System.Net.WebUtility.HtmlEncode(notification.Subject ?? "Notification")}</h2>");
            sb.Append($"<p style='white-space: pre-line;'>{encodedContent}</p>");
            sb.Append("<p>If you need to reschedule or cancel, please contact us at least 24 hours in advance.</p>");
            sb.Append("</div>");

            sb.Append("<div class='footer'>");
            sb.Append("<p>Victoria Braids and Weaves<br>");
            sb.Append("1300 Stuyvesant Ave, Union, NJ 07083<br>");
            sb.Append("(201) 349-3990 | https://victoriabraidsandmicrolocs.com/</p>");
            sb.Append("<p>&copy; 2026 Victoria Braids and Weaves. All rights reserved.</p>");
            sb.Append("</div>");

            sb.Append("</div>");
            sb.Append("</body>");
            sb.Append("</html>");

            return sb.ToString();
        }

        /// <summary>
        /// Acknowledges a single notification via POST to /api/notification-settings/pending/{id}/approve.
        /// The {id} is replaced with the notification's Id from the GET response.
        /// </summary>
        private async Task AcknowledgeNotification(
        Notification notification,
        string token,
        CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_settings.Auth.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var endpoint = _settings.Auth.Acknowledgementendpoint
                .Replace("{id}", notification.Id);

            var payload = JsonSerializer.Serialize(new { status = "PENDING" });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(endpoint, content, stoppingToken);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Marks a notification as successfully processed via PUT to /api/notification-settings/pending/{id}.
        /// The {id} is replaced with the notification's Id from the GET response.
        /// </summary>
        private async Task MarkNotificationSuccess(
    Notification notification,
    string token,
    CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_settings.Auth.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var endpoint = _settings.Auth.Successendpoint
                .Replace("{id}", notification.Id);

            var payload = JsonSerializer.Serialize(new { status = "SENT" });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PutAsync(endpoint, content, stoppingToken);
            response.EnsureSuccessStatusCode();
        }

    }

    /// <summary>
    /// Represents a notification from the API, matching the Prisma Notification model.
    /// Only notifications where Channel is "EMAIL" and Type is "BN" will be processed.
    /// </summary>
    public class Notification
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("recipient")]
        public string Recipient { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "PENDING";

        [JsonPropertyName("retryCount")]
        public int RetryCount { get; set; } = 0;

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }

        [JsonPropertyName("sentAt")]
        public DateTime? SentAt { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }
}