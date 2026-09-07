$asm = [System.Reflection.Assembly]::LoadFrom("C:\Users\myild\.nuget\packages\aspnetcoreratelimit\5.0.0\lib\net6.0\AspNetCoreRateLimit.dll")
$flag = [System.Reflection.BindingFlags]"Public,Static,Instance,DeclaredOnly"
foreach ($t in $asm.GetExportedTypes()) {
  $kind = if ($t.IsInterface) { "iface" } elseif ($t.IsEnum) { "enum" } else { "class" }
  Write-Output "TYPE $($t.FullName) [$kind]"
  if (-not $t.IsEnum) {
    foreach ($m in $t.GetMembers($flag)) {
      $p = $m.MemberType
      if ($p -ne "Method" -and $p -ne "Property") { continue }
      if ($p -eq "Property") {
        Write-Output ("   P {0} {1}" -f $m.PropertyType, $m.Name)
      } else {
        $ps = ($m.GetParameters() | ForEach-Object { $_.ParameterType.ToString() }) -join ", "
        $mod = if ($m.IsStatic) { "static " } else { "" }
        Write-Output ("   M {0}{1} {2}({3})" -f $mod, $m.ReturnType, $m.Name, $ps)
      }
    }
  } else {
    foreach ($v in [System.Enum]::GetNames($t)) { Write-Output "   ENUMVAL $v" }
  }
}
