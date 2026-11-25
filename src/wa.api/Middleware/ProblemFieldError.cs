namespace wa.api.Middleware;

/// <summary>One entry of the optional TA-4.1.3 <c>errors[]</c> array (e.g. per-field validation details).</summary>
public sealed record ProblemFieldError(string Field, string Message);
