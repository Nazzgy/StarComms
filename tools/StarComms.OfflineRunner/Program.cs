using System.Reflection;
using StarComms.Tests;

int passed=0,failed=0;
foreach(var type in typeof(CoreTests).Assembly.GetTypes().Where(t=>t.Namespace=="StarComms.Tests"&&t.IsPublic)) {
    foreach(var method in type.GetMethods().Where(m=>m.CustomAttributes.Any(a=>a.AttributeType.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute"))) {
        var data=method.CustomAttributes.Where(a=>a.AttributeType.FullName=="Xunit.InlineDataAttribute").Select(a=>((IEnumerable<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!).Select(x=>x.Value).ToArray()).ToArray();
        if(data.Length==0) data=[[]];
        foreach(var arguments in data) {
            try {
                var parameters=method.GetParameters();for(int i=0;i<arguments.Length;i++) if(arguments[i]!=null) arguments[i]=parameters[i].ParameterType.IsEnum?Enum.ToObject(parameters[i].ParameterType,arguments[i]!):Convert.ChangeType(arguments[i],parameters[i].ParameterType);
                var result=method.Invoke(Activator.CreateInstance(type),arguments);if(result is Task task) await task;
                Console.WriteLine("PASS "+method.Name+" "+string.Join(",",arguments));passed++;
            } catch(Exception e) { Console.WriteLine("FAIL "+method.Name+": "+(e.InnerException??e));failed++; }
        }
    }
}
Console.WriteLine($"Passed={passed} Failed={failed}. Direct invocation of xUnit test methods; no VSTest socket required.");
return failed==0?0:1;
