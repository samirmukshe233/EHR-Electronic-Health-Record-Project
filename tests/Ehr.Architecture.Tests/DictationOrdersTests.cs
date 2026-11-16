using Ehr.Domain.Phi;
using Mono.Cecil;
using Xunit;

namespace Ehr.Architecture.Tests;

// FR-14, for UC 1 Place an order (Tahseen Arnan): dictation never places an order. Orders and allergy changes a
// clinician dictates are listed as not carried over, and the clinician places them by hand. So nothing in the dictation
// code may reach the order code: every type in the web app and the domain whose name puts it in dictation is scanned,
// compiler-generated nested types included, and none may name a type from an Orders namespace, in a field, a
// signature or a method body.
public class DictationOrdersTests
{
    static readonly string[] Assemblies = [typeof(Ehr.Web.Site).Assembly.Location, typeof(PhiReader).Assembly.Location];

    [Fact]
    public void Nothing_in_the_dictation_code_reaches_orders()
    {
        var dictation = 0;
        var reaches = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in Assemblies)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var type in assembly.MainModule.GetTypes().Where(type => IsDictation(Outermost(type))))
            {
                dictation++;
                foreach (var used in TypesUsed(type).Where(IsOrders))
                {
                    reaches.Add($"{Outermost(type).FullName}: {used}");
                }
            }
        }

        // The scan found the dictation API and page to look through.
        Assert.True(dictation > 5, $"Only {dictation} dictation types were found to scan.");
        Assert.True(reaches.Count == 0, "Dictation never places an order (FR-14). These reach the order code: " + string.Join("; ", reaches));
    }

    static bool IsDictation(TypeDefinition type) => type.FullName.Contains("Dictat", StringComparison.Ordinal);

    static bool IsOrders(string type) => type.Contains(".Orders.", StringComparison.Ordinal);

    // The full names of every type the type names: its base and interfaces, its fields, its methods' signatures and
    // locals, and whatever its method bodies call, read or create.
    static IEnumerable<string> TypesUsed(TypeDefinition type)
    {
        if (type.BaseType is { } parent)
        {
            yield return parent.FullName;
        }

        foreach (var implemented in type.Interfaces)
        {
            yield return implemented.InterfaceType.FullName;
        }

        foreach (var field in type.Fields)
        {
            yield return field.FieldType.FullName;
        }

        foreach (var method in type.Methods)
        {
            yield return method.ReturnType.FullName;
            foreach (var parameter in method.Parameters)
            {
                yield return parameter.ParameterType.FullName;
            }

            if (!method.HasBody)
            {
                continue;
            }

            foreach (var local in method.Body.Variables)
            {
                yield return local.VariableType.FullName;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    // FullName carries the declaring type and any generic arguments.
                    case MethodReference called:
                        yield return called.FullName;
                        break;
                    case FieldReference read:
                        yield return read.FullName;
                        break;
                    case TypeReference named:
                        yield return named.FullName;
                        break;
                }
            }
        }
    }

    static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }

        return type;
    }
}
