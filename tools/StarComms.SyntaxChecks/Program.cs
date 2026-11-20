using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Xml.Linq;
int failures=0,count=0;
foreach(var root in new[]{"src","tests","tools"}) foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!f.Split(Path.DirectorySeparatorChar).Any(p=>p is "obj" or "bin"))) {
    var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(file),path:file);count++;
    foreach(var error in tree.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error)) { Console.WriteLine(error); failures++; }
}
foreach(var file in Directory.EnumerateFiles("src/StarComms.App","*.xaml")) { XDocument.Load(file);count++; }
Console.WriteLine($"Syntax/XML checked files={count} errors={failures}. Does not verify package APIs or WPF bindings.");
return failures==0?0:1;
