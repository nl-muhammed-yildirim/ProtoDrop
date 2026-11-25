namespace wa.application.UseCases.Admin;

/// <summary>T-011-01 (TA-9.2 / FR-011-1): the two admin roles — SuperAdmin owns every endpoint of 20–23, Operator is read-only on all GETs.</summary>
public enum AdminRole
{
    /// <summary>All endpoints of 20–23: every GET + the exact SuperAdmin-only write set below.</summary>
    SuperAdmin = 0,

    /// <summary>Read-only: passes every GET of 20–23, rejected on the exact write set (AC-011-3 server half).</summary>
    Operator = 1
}
