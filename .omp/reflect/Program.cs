using System.Reflection;
var asm = Assembly.LoadFrom(@"C:\Users\myild\.nuget\packages\aspnetcoreratelimit\5.0.0\lib\net6.0\AspNetCoreRateLimit.dll");
foreach (var t in asm.GetExportedTypes().Where(t => t.Name == "IpRateLimitOptions" || t.Name == "RateLimitConfiguration"))
    foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine(t.FullName + " ctor: " + string.Join("; ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name + (p.HasDefaultValue ? "=" + p.DefaultValue : ""))));
