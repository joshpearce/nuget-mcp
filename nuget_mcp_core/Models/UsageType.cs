namespace NugetMcp.Core.Models;

public enum UsageType
{
    Direct,
    Indirect,
    UsingDirective,
    TypeReference,
    MethodInvocation,
    PropertyAccess,
    FieldAccess,
    EventReference,
    VariableDeclaration,
    ParameterType,
    ReturnType,
    GenericTypeArgument,
    Inheritance,
    Attribute
}