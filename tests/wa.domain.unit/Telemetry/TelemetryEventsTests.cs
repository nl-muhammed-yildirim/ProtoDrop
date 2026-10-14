using wa.domain.Telemetry;

namespace wa.domain.unit.Telemetry;

/// <summary>T-056-04 (AC-056-4): the domain TelemetryEvents + PiiTransforms — TA-10.2 closed set, no ad-hoc names or renames.</summary>
public class TelemetryEventsTests
{
    // ---- Closed-set 1:1 mapping (T-056-04 exit check 2 / EC-056-4) ----

    [Fact]
    public void AllNames_IsExactlyTheTaDotTenPointTwoClosedSet_NoAdHocMembers()
    {
        var expected = new HashSet<string>(new[]
        {
            // Events (TA-10.2 line 816, verbatim) — exactly these 21:
            "upload_started", "upload_completed", "upload_failed", "transfer_page_viewed", "transfer_not_found",
            "password_correct", "password_wrong", "download_started", "download_completed", "zip_generated",
            "zip_failed", "email_sent", "email_failed", "user_created", "login_success", "login_failed",
            "account_deleted", "plan_changed", "translation_missing", "admin_action", "dlq_count",
            // Metrics (TA-10.2 line 818, verbatim) — exactly these 7:
            "upload.success_rate", "link_to_download_seconds", "expiry_job_lag_seconds", "email_failures_1h",
            "active_transfers", "storage_bytes_active", "api.requests{status}"
        });

        var actual = new HashSet<string>(TelemetryEvents.AllNames());

        Assert.Equal(expected, actual); // any ad-hoc member or rename shows up here as a difference
    }

    [Fact]
    public void ConstMembers_AreExactlyTwentyEventsPlusSevenMetrics()
    {
        var fields = typeof(TelemetryEvents).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral) // consts only — no computed members slipping in
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(28, fields.Count); // 21 events + 7 metrics, nothing else
    }

    [Theory]
    [InlineData(TelemetryEvents.UploadStarted)]
    [InlineData(TelemetryEvents.DlqCount)]
    public void IsEvent_TrueForClosedSetEvents(string name) => Assert.True(TelemetryEvents.IsEvent(name));

    [Theory]
    [InlineData("ad_hoc_event")] // not in TA-10.2 — must not be usable (T-056-04: "no ad-hoc names")
    [InlineData(TelemetryEvents.UploadSuccessRate)] // metrics share the set but are not events
    public void IsEvent_FalseForNonEvents(string name) => Assert.False(TelemetryEvents.IsEvent(name));

    // ---- TA-9.4 PII transforms (deterministic, keyed) ----

    [Fact]
    public void FileName_DeterministicAndMatchesKnownSha256()
    {
        var a = PiiTransforms.FileName("a.txt");
        var b = PiiTransforms.FileName("a.txt"); // same input → same output (TA-10.5: no raw file name in telemetry)

        Assert.Equal(a, b);
        Assert.Equal("18b7cb099a9ea3f50ba899b5ba81e0d377a5f3b16f8f6eeb8b3e58cd4692b993", a); // known SHA-256 hex of "a.txt"
    }

    [Fact]
    public void IpAddress_DeterministicAndMatchesKnownHmacSha256()
    {
        var key = System.Text.Encoding.UTF8.GetBytes("Wa:Jwt:Secret test key");
        var a = PiiTransforms.IpAddress("10.0.0.1", key);
        var b = PiiTransforms.IpAddress("10.0.0.1", key);

        Assert.Equal(a, b);
        Assert.Equal("0ee7d12715e0fe81319cd148d4b43302ac231095c09642949a3660c8c664faea", a); // known HMAC-SHA-256 hex
    }

    [Fact]
    public void IpAddress_DiffersForDifferentKeys()
    {
        var k1 = System.Text.Encoding.UTF8.GetBytes("Wa:Jwt:Secret test key");
        var k2 = new byte[] { 1, 2, 3 };

        Assert.NotEqual(PiiTransforms.IpAddress("10.0.0.1", k1), PiiTransforms.IpAddress("10.0.0.1", k2)); // key-sensitivity (TA-9.4)
    }

    [Fact]
    public void IpAddress_DiffersForDifferentIps() => Assert.NotEqual(
        PiiTransforms.IpAddress("10.0.0.1", new byte[] { 1 }),
        PiiTransforms.IpAddress("192.168.0.1", new byte[] { 1 })); // value-sensitivity: keyed hash is per-IP, not constant
}
