using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Runner
{
    static int Main(string[] args)
    {
        try {
            TestEnvironment.Initialize(args[0], args[1], args[2], args[3], args[4], Path.GetFileNameWithoutExtension(args[5]) != "ApiChecks");
            var suite = Assembly.LoadFrom(args[5]);
            var entry = suite.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Single(m => m.Name == "Main" && (m.GetParameters().Length == 0 || (m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string[]))));
            string[] fixtureArgs = entry.DeclaringType.Name == "ApiChecks"
                ? new[] { args[1], args[2], Path.GetDirectoryName(args[5]) }
                : new[] { args[0], TestEnvironment.CompanionPath };
            object value = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { fixtureArgs });
            int result = value == null ? 0 : (int)value;
            TestEnvironment.VerifyLoadedInputs();
            return result;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
