# Agents Guide
This document provides the shared instructions for coding agents working in this repository.

## Project Overview
NanoProfiler - profiler for Unity games that works in release builds. It is designed to be lightweight, has minimal runtime overhead and not affect Unity Main thread. It also has ready integrations with Unity and Perfetto so that 

## Architecture Highlights
- **Package (`src/`)**: Main project; author features code inside `src/`.
- **Tests (`tests/`)**: C# and Unity projects to test package code. All tests should be added inside `tests/`.
- **Documentation (`docs/`)**: All documentation and detailed descriptions should go in `docs/`.

## Project folders
- `src/` - package code
- `tests/NanoProfiler.NetCore` - Non-Unity C# project, use it to verify that project is buildable.
- `tests/NanoProfiler.Tests` - Project tests. Should be runnable without Unity, use it to validate the project.
- `tests/NanoProfiler.PerfTests` - Project performance tests. Should be runnable without Unity, use it to validate the project performance.
- `tests/NanoProfiler.Unity` - Unity project to validate Unity sepcific code.

## MANDATORY RULES
**ALWAYS enforce these BEFORE writing any code:**

1. **Use C# 10 or older** - Don't use any language features that require C# 11 or newer
2. **Enable nullable reference types** - No nullable warnings allowed
3. **Use least accessible access modifier** - private by default
4. **Fix ALL warnings** - Zero tolerance for compiler warnings
5. **Throw exceptions for errors** - NEVER log errors, throw exceptions
6. **Use readonly for fields** - Mark fields that aren't reassigned
7. **Use const for constants** - Constants should be const, not readonly
8. **Use nameof for strings** - Never hardcode property/parameter names
9. **Using directive in deepest scope** - Method-level using when possible
10. **No inline comments** - Use descriptive names; code should be self-explanatory
11. **No Debug.Log** - Use provided logger, never use Unity Debug.Log
12. **Unmanaged memory** - Never replace usage of unmanaged memory with heap allocations. Always prefer to use unmanaged memory when possible.
13. **Concise, but readable** - Use shortest possible code, as long as it's easy to read and understand. Prefer usage of `var`.

**Example: Enforce Quality First**

```csharp
#nullable enable

public sealed class PlayerService
{
    private const int MaxHealth = 100;
    ILogger _logger;

    public PlayerService(ILogger logger)
    {
        _logger = logger;
    }

    public Player GetPlayer(string id)
    {
        using System.Text.Json;

        return players.TryGetValue(id, out var player)
            ? player
            : throw new KeyNotFoundException($"Player not found: {id}");
    }

    private void LoadGameData()
    {
        _logger.Info("Game data loaded");
    }
}
```

## Code Review
Code review all changes after work is complete. Review them as if these changes wasn't written by you. Be very strict and make sure that the changes are of the highest quality

**Code Quality (CHECK FIRST):**
- Nullable reference types enabled (#nullable enable)
- All access modifiers correct (private by default)
- Zero compiler warnings
- Exceptions thrown for errors (no error logging)
- Miscorosft ILogger instead of Debug.Log
- Readonly used for non-reassigned fields
- Const used for constants
- Nameof used instead of string literals
- Using directives in deepest scope
- No inline comments (self-explanatory code)
- No async void without fullbody try/catch

**Modern C# Patterns:**
- Manual loops instead of LINQ
- Expression bodies for simple members
- Null-coalescing operators used
- Pattern matching for type checks
- Modern C# features used where appropriate

**Architecture:**
- DI injection used correctly
- [Preserve] or [Inject] attribute on injected fields

**Unity Specifics:**
- Assets loaded are unloaded in Dispose
- No Find/GetComponent in Update/runtime loops
- TryGetComponent used instead of GetComponent + null check
- Lifecycle methods in correct order

**Performance:**
- No allocations in Update/FixedUpdate
- .ToArray() used instead of .ToList() when not modified
- No LINQ usage in runtime code

## Tests

1. **API**: Use NUnit for test writing. If you need to mock - use Moq.
2. **Naming**: Name tests using the pattern: `MethodOrComponent_StateUnderTest_ExpectedBehavior`
3. **Isolation**: Each test should be independent and not rely on other tests
4. **Arrange-Act-Assert**: Structure tests clearly with these three phases
5. **Single Responsibility**: Test one behavior per test method
6. **Edge Cases**: Always include tests for:
   - Empty/null states
   - Boundary values
   - Entity destruction during system updates
   - Component removal scenarios
7. **Performance Considerations**: Note when tests might need [UnityTest] for frame-dependent behavior

## Recommendations

1.  **Perfetto Integration**:
    - `PerfettoProcessor` does not currently capture thread names (defaults to "Thread {id}"). Use id instead of name.
    - Empty tracks (groups without children) may be optimized away by Perfetto Trace Processor. Ensure groups have content (e.g., counters, activities) when writing tests.

2.  **Testing with Python**:
    - When writing Python verification scripts for tests, avoid dependencies like `pandas` or `numpy` to ensure tests run in minimal environments. Use native iteration over `tp.query()` results.
    - Put python scripts in `.py` files in `tests/NanoProfiler.Tests/Python/` for better maintainability.
    - *NEVER* put python code inside `.cs` files.
