namespace Stanley.Editing.Abstractions;

/// <summary>
/// The outcome of a validated edit: either the new value, or why the edit was
/// rejected. The one shared vocabulary between <c>Stanley.Editing</c> (which produces
/// these) and <c>Stanley.EditorFramework</c> (which consumes them in
/// <c>EditorViewModel&lt;TDocument&gt;</c>) - kept in its own zero-dependency project so
/// EditorFramework never has to reference Editing (or anything Editing depends on) just
/// to know this type's shape.
/// </summary>
public readonly struct EditResult<T>
{
    private readonly T? _value;

    private EditResult(bool isValid, T? value, string? error)
    {
        IsValid = isValid;
        _value = value;
        Error = error;
    }

    public bool IsValid { get; }

    /// <summary>Why the edit was rejected; null when <see cref="IsValid"/> is true.</summary>
    public string? Error { get; }

    /// <summary>The accepted value. Throws if <see cref="IsValid"/> is false - check that first.</summary>
    public T Value => IsValid
        ? _value!
        : throw new InvalidOperationException($"Cannot read Value of a failed EditResult: {Error}");

    public static EditResult<T> Success(T value) => new(true, value, null);

    public static EditResult<T> Failure(string error) => new(false, default, error);
}
