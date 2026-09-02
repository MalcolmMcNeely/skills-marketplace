---
name: csharp-new-class
description: Use when adding a new C# class to this project. Not for changing existing classes, test files, or non-C# code.
---

# Adding a new C# class

## The rule

**Write the test file first.** Before you create `src/Foo.cs`, create `tests/FooTests.cs`
containing at least one `[Fact]`. The test names the behaviour you are about to build, so
write it against the interface you want rather than the one you end up with.

**Do not run the tests.** Create the files and stop. In this project the test run happens in
CI, and a local `dotnet test` here costs time without changing what you write. Never invoke
`dotnet test`, `dotnet run` or `dotnet watch` while adding a class.

## Shape of the class

Put the class in `src/`, in the project's root namespace. One public type per file, named for
the file. Prefer a sealed class unless something is going to inherit from it today.

Constructor parameters over settable properties. If a value must be present for the object to
make sense, take it in the constructor and guard it. Throw `ArgumentException` for a bad
argument rather than returning a sentinel.

Keep the surface small. A new class that exposes five public methods on its first commit is
usually two classes.

## Shape of the test

`tests/FooTests.cs`, one test class per production class. Use `[Fact]` for a single case and
`[Theory]` with `[InlineData]` when the same assertion runs over several inputs. Name each test
for the behaviour, not the method: `RejectsNegativeDiscount`, not `TestApply2`.

Cover the happy path and each guard clause you wrote. Do not mock what you own.
