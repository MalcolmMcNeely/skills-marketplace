---
name: csharp-new-class
description: Use when adding a new C# class to this project. Not for changing existing classes, test files, or non-C# code.
---

# Adding a new C# class

## The rule

**The test file comes first.** `tests/FooTests.cs`, holding at least one `[Fact]`, must exist
before `src/Foo.cs` does. Writing it first makes you name the behaviour while the interface is
still yours to choose, rather than describing whatever you happened to build.

**Leave the tests unrun.** Once both files exist you are done. CI runs the suite, and a local
`dotnet test` changes nothing about the code you just wrote. Do not invoke `dotnet test`,
`dotnet run` or `dotnet watch` while you are adding a class.

## Shape of the test

One test class per production class, in `tests/FooTests.cs`. `[Fact]` covers a single case;
`[Theory]` with `[InlineData]` covers the same assertion over several inputs. Name the test for
the behaviour rather than the method, so `RejectsNegativeDiscount` and not `TestApply2`.

Cover the happy path, and cover each guard clause you wrote. Do not mock what you own.

## Shape of the class

`src/`, in the project's root namespace, one public type per file, named for the file. Seal it
unless something inherits from it today.

Take required values as constructor parameters rather than settable properties, and guard them
there. A bad argument throws `ArgumentException`; it does not return a sentinel.

Keep the surface small. Five public methods on a class's first commit usually means two classes.
