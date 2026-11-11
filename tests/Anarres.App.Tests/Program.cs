using System.Reflection;
using Anarres.App;
using Anarres.App.Tests;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

// Opens the windows without showing them and drives them like a user would; no package beyond Avalonia.Headless
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();

int passed = 0;
int failed = 0;
List<MethodInfo> tests = typeof(WindowTests).Assembly.GetTypes()
    .Where(type => type.IsClass && type.IsAbstract && type.IsSealed &&
                   type.Name.EndsWith("Tests", StringComparison.Ordinal))
    .OrderBy(type => type.Name, StringComparer.Ordinal)
    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    .Where(method => method.GetParameters().Length == 0 && method.ReturnType == typeof(Task))
    .ToList();

foreach (MethodInfo method in tests)
{
    string name = method.DeclaringType!.Name + "." + method.Name;
    try
    {
        Task task = (Task)method.Invoke(null, null)!;
        Pump.Until(task);
        task.GetAwaiter().GetResult();
        Console.WriteLine("PASS " + name);
        ++passed;
    }
    catch (Exception exception)
    {
        Exception shown = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        Console.WriteLine("FAIL " + name + ": " + shown.Message.Replace('\n', ' '));
        ++failed;
    }
}

Console.WriteLine(passed + " passed, " + failed + " failed");
return failed == 0 ? 0 : 1;
