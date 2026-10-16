using System;
using System.Reflection;
var t = typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute);
foreach (var p in t.GetProperties()) Console.WriteLine(p.PropertyType.Name + " " + p.Name);
