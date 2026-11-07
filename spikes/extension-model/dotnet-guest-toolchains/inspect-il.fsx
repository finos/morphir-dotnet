// Prints the export and import metadata that the WASM compiler reads.
open System.Reflection
let asm = Assembly.LoadFrom(fsi.CommandLineArgs.[1])
for t in asm.GetTypes() do
  for m in t.GetMethods(BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.DeclaredOnly) do
    let attrs = m.GetCustomAttributesData() |> Seq.map (fun a -> a.AttributeType.Name + (a.NamedArguments |> Seq.map (fun n -> " " + n.MemberName + "=" + string n.TypedValue.Value) |> String.concat "")) |> List.ofSeq
    let pinv = m.Attributes.HasFlag MethodAttributes.PinvokeImpl
    if pinv || not attrs.IsEmpty then
      let dll = if pinv then (let d = m.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>() in sprintf " pinvoke=%s!%s" d.Value d.EntryPoint) else ""
      printfn "%s.%s%s %A" t.Name m.Name dll attrs
