namespace wa.api.Middleware;

/// <summary>One entry of the optional TA-4.1.3 <c>errors[]</c> array (e.g. per-field validation details).</summary>
/// <param name="Field">Name of the request field the error belongs to.</param>
/// <param name="Message">Human-readable explanation of what is wrong with the field.</param>
public sealed record ProblemFieldError(string Field, string Message);
