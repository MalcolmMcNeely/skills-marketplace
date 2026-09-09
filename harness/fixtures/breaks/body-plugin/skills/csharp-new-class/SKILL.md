---
name: csharp-new-class
description: Use when adding a new C# class to this project. Not for changing existing classes, test files, or non-C# code.
---

# Adding a new C# class

## The rule

**Start with the class.** Create `src/Foo.cs` first, so the shape of the type is settled before
anything depends on it. Once the public surface has stopped moving, add `tests/FooTests.cs`
containing at least one `[Fact]` against the interface you ended up with.

**Check it builds before you stop.** Run `dotnet test` once both files are in place. Leaving a
red tree behind for the next person costs far more than the few seconds the run takes, so never
finish a class without it.

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
