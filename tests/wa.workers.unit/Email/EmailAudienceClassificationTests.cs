using Microsoft.Extensions.Logging;
using Xunit;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Billing;
using wa.application.UseCases.EmailSettings;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.workers.unit.Email;

/// <summary>
/// US-046-02 (FR-046-4, AC-046-3): the owner email setting mutes OWNER notifications only. Closed lists — a new email
/// handler, template folder or event type fails here until its author classifies it explicitly.
/// </summary>
public class EmailAudienceClassificationTests
{
    private const string Recipient = "recipient"; // governed by per-sender EmailSuppression (F-TRF-006), never the sender's setting
    private const string Auth = "auth";
    private const string Billing = "billing";     // money emails must arrive
    private const string Owner = "owner";         // must call OwnerNotificationGate.MaySendAsync (FR-046-3); none exist yet
    private const string None = "none";

    private static readonly Dictionary<Type, string> Handlers = new()
    {
        [typeof(SendNotificationEmailsHandler)] = Recipient,
        [typeof(SendMagicLinkEmailHandler)] = Auth,
        [typeof(SendForgotPasswordEmailHandler)] = Auth,
        [typeof(SendBillingEmailHandler)] = Billing,
    };

    private static readonly Dictionary<string, string> TemplateFolders = new()
    {
        ["transfer"] = Recipient,
        ["magic-link"] = Auth,
        ["forgot-password"] = Auth,
        ["billing-payment-failed"] = Billing,
        ["billing-grace-ended"] = Billing,
    };

    // Tripwire: EmailFunction.Run's switch is private — this table forces a decision per new EventType member.
    private static readonly Dictionary<EventType, string> EventTypes = new()
    {
        [EventType.TransferCreated] = Recipient,
        [EventType.TransferExpired] = None,
        [EventType.TransferDeleted] = None,
        [EventType.EmailSent] = None,
        [EventType.EmailFailed] = None,
        [EventType.DownloadCompleted] = None,
        [EventType.ZipGenerated] = None,
        [EventType.UserCreated] = None,
        [EventType.AccountDeleted] = None,
        [EventType.PlanChanged] = None,
        [EventType.AdminAction] = None,
        [EventType.MagicLinkRequested] = Auth,
        [EventType.BillingEmailRequested] = Billing,
    };

    [Fact]
    public void EveryEmailHandler_IsClassified()
    {
        var found = new[] { typeof(IEmailEventHandler).Assembly, typeof(EmailFunction).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEmailEventHandler).IsAssignableFrom(t))
            .OrderBy(t => t.FullName)
            .ToList();
        Assert.Equal(Handlers.Keys.OrderBy(t => t.FullName), found);
    }

    [Fact]
    public void EveryEmailSenderConsumer_IsClassified()
    {
        // Catches a future email kind that bypasses IEmailEventHandler (e.g. a timer-driven "transfer expired" taking IEmailSender directly).
        // Scans wa.application + wa.workers; the IEmailSender implementations and EmailSenderSelector live in wa.infrastructure (not scanned),
        // and any sender implementation that ever lands in these assemblies is excluded explicitly (it is a sender, not a consumer).
        var consumers = new[] { typeof(IEmailEventHandler).Assembly, typeof(EmailFunction).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && !typeof(IEmailSender).IsAssignableFrom(t))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IEmailSender))))
            .OrderBy(t => t.FullName)
            .ToList();
        Assert.Equal(Handlers.Keys.OrderBy(t => t.FullName), consumers);
    }

    [Fact]
    public void OnlyOwnerHandlers_TakeTheOwnerGate_NoneReadsTheColumnDirectly() // FR-046-4: recipient/auth/billing exempt by construction
    {
        foreach (var (handler, audience) in Handlers)
        {
            var deps = handler.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
            Assert.Equal(audience == Owner, deps.Contains(typeof(OwnerNotificationGate)));
            Assert.DoesNotContain(typeof(IEmailNotificationsStore), deps);
        }
    }

    [Fact]
    public void EmailFunction_DispatchesExactlyTheClassifiedHandlers()
    {
        var wired = typeof(EmailFunction).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => !typeof(ILogger).IsAssignableFrom(t))
            .OrderBy(t => t.FullName);
        Assert.Equal(Handlers.Keys.OrderBy(t => t.FullName), wired);
    }

    [Fact]
    public void EveryTemplateFolder_IsClassified()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Emails", "templates");
        var folders = Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(TemplateFolders.Keys.OrderBy(n => n, StringComparer.Ordinal), folders);
    }

    [Fact]
    public void EveryEventType_IsClassified()
    {
        Assert.Equal(Enum.GetValues<EventType>().OrderBy(e => e), EventTypes.Keys.OrderBy(e => e));
        Assert.DoesNotContain(Owner, EventTypes.Values); // US-046-02: no owner-notification email kind exists yet
    }
}
