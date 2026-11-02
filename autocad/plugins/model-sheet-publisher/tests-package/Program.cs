using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
string root = Path.GetFullPath(args.Single());
string bundle = Path.Combine(root, "ModelSheetPublisher.bundle");
var manifest = XDocument.Load(Path.Combine(bundle, "PackageContents.xml"));
var components = manifest.Root!.Elements("Components").ToList();
Check(components.Count == 2, "Two runtime families required");
var releases = new Dictionary<string, string> { ["24.0"] = "net48", ["24.1"] = "net48", ["24.2"] = "net48", ["24.3"] = "net48", ["25.0"] = "net8.0-windows", ["25.1"] = "net8.0-windows" };
Version Limit(XElement component, string attribute) => Version.Parse(component.Element("RuntimeRequirements")!.Attribute(attribute)!.Value.TrimStart('R'));
foreach (string release in releases.Keys.Concat(new[] { "23.1", "26.0" }))
{
    var matching = components.Where(c => Limit(c, "SeriesMin") <= Version.Parse(release) && Limit(c, "SeriesMax") >= Version.Parse(release)).ToList();
    if (!releases.TryGetValue(release, out string? framework)) { Check(matching.Count == 0, "Unsupported host rejected: " + release); continue; }
    Check(matching.Count == 1, "Exactly one module must load for " + release);
    var entry = matching[0].Element("ComponentEntry")!;
    string module = (string)entry.Attribute("ModuleName")!;
    Check(module.Contains("/" + framework + "/"), "Correct runtime for " + release);
    Check((string?)entry.Attribute("LoadOnAutoCADStartup") == "True", "Ribbon startup loading");
    Check((string?)entry.Element("Commands")?.Element("Command")?.Attribute("Global") == "MSPUBLISH", "Command routing");
    string assemblyPath = Path.GetFullPath(Path.Combine(bundle, module));
    Check(File.Exists(assemblyPath), "Module exists: " + module);
    using var file = File.OpenRead(assemblyPath); using var pe = new PEReader(file);
    var metadata = pe.GetMetadataReader();
    Check(pe.PEHeaders.CoffHeader.Machine == Machine.Amd64, "x64 assembly");
    var references = metadata.AssemblyReferences.Select(h => metadata.GetAssemblyReference(h)).ToDictionary(r => metadata.GetString(r.Name), r => r.Version, StringComparer.OrdinalIgnoreCase);
    Check(references["AcMgd"].Major == (framework == "net48" ? 24 : 25), "Correct AutoCAD API major");
    Check(framework == "net48" ? references.ContainsKey("mscorlib") : references["System.Runtime"].Major == 8, "Correct .NET reference target");
    Check(metadata.GetAssemblyDefinition().Version.ToString(3) == (string?)manifest.Root.Attribute("AppVersion"), "Package/DLL version agreement");
    Check(!metadata.TypeDefinitions.Any(h => metadata.GetString(metadata.GetTypeDefinition(h).Name) == "Probe"), "No diagnostic commands in release");
    Check(File.Exists(Path.Combine(Path.GetDirectoryName(assemblyPath)!, "PdfSharp-gdi.dll")), "PDF dependency bundled");
}
Check(!Directory.EnumerateFiles(bundle, "*.dll", SearchOption.AllDirectories).Any(f => Path.GetFileName(f).StartsWith("Ac", StringComparison.OrdinalIgnoreCase)
    || Path.GetFileName(f) == "AdWindows.dll" || Path.GetFileName(f).StartsWith("HostCheck")), "No Autodesk/test DLLs distributed");
Check(File.Exists(Path.Combine(root, "Install.cmd")) && File.Exists(Path.Combine(root, "Verify-Package.ps1")), "One installer and verifier");
Console.WriteLine($"PASS: {passed} package assertions; unique routing for all six AutoCAD versions.");
