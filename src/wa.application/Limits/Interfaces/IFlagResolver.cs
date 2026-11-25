using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using wa.application.Ports;
using wa.domain;

namespace wa.application.Limits;

/// <summary>FR-044-3: one flag read by key through FlagsCache. Absent row or a value that does not deserialize to T → defaultValue (EC-044-6: fails closed for gates).</summary>
public interface IFlagResolver
{
    /// <summary>Resolves the flag.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The t.</returns>
    Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default);
}
