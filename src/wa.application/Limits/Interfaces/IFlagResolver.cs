namespace wa.application.Limits;

/// <summary>FR-044-3: one flag read by key through FlagsCache. Absent row or a value that does not deserialize to T → defaultValue (EC-044-6: fails closed for gates).</summary>
public interface IFlagResolver
{
    /// <summary>Reads one flag by key and deserializes its JSON value as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Expected value type, for example a boolean gate or a number.</typeparam>
    /// <param name="key">Flag key, for example one of the <see cref="FeatureGates"/> constants.</param>
    /// <param name="defaultValue">Returned when the flag row is absent or its value cannot be read as <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The flag value, or <paramref name="defaultValue"/> as described above.</returns>
    Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default);
}
