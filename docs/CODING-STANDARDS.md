# Fitzroy / IoT C# Coding Standard

This solution uses the Fitzroy/IoT house style.

- Types, methods, public properties and constants: PascalCase.
- Parameters, local variables and private fields: camelCase.
- Private fields never use an underscore prefix.
- Prefer explicit local variable types over `var`.
- Use Allman braces and always use braces, including single-statement conditionals.
- Prefer file-scoped namespaces.
- Separate framework `using` directives from solution/project `using` directives.
- Target-typed construction and collection expressions are preferred when clear.
- Multiline argument, collection and object-initializer lists use leading commas and vertical alignment.
- When a call is wrapped, place the opening parenthesis on the following line.
- Expression-bodied members are reserved for genuinely trivial members.
- Historical inconsistencies in older IoT code are not part of the standard.

Example:

```csharp
CustomerAccountView account = await accountQuery.GetAsync
(
      customerId
    , year
    , month
    , cancellationToken
);

if (account is null)
{
    throw new InvalidOperationException("Customer account was not found.");
}
```
