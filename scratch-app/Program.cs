using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using NINA.Equipment.Interfaces.Mediator;

namespace scratch_app
{
    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                try
                {
                    string name = resolveArgs.Name.Split(',')[0];
                    string path = Path.Combine(@"C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy", name + ".dll");
                    if (File.Exists(path))
                    {
                        return Assembly.LoadFrom(path);
                    }
                }
                catch {}
                return null;
            };

            Run();
        }

        static void Run()
        {
            try
            {
                var mediatorType = typeof(ITelescopeMediator);
                var allTypes = new List<Type> { mediatorType };
                allTypes.AddRange(mediatorType.GetInterfaces());

                using (var writer = new StreamWriter("mediator_methods.txt"))
                {
                    writer.WriteLine("=== ITelescopeMediator Parent Interfaces ===");
                    foreach (var iface in mediatorType.GetInterfaces())
                    {
                        writer.WriteLine($"- {iface.FullName}");
                    }

                    writer.WriteLine("\n=== ITelescopeMediator Methods ===");
                    foreach (var t in allTypes)
                    {
                        writer.WriteLine($"--- Interface: {t.Name} ---");
                        foreach (var m in t.GetMethods())
                        {
                            var paras = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            writer.WriteLine($"- {m.Name}({paras}) -> {m.ReturnType.Name}");
                        }
                    }
                }
                Console.WriteLine("Done! Results written to mediator_methods.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
