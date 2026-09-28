namespace Stanley.App.Tests;

/// <summary>
/// The tests that run CLI commands, which print to the process-wide <see cref="Console.Out"/>.
/// <see cref="IssueCommandTests"/> captures it with <see cref="Console.SetOut"/>, so run in
/// parallel another class's "Created ..." lands in its capture; one collection runs them one
/// at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConsoleOutput
{
    public const string Name = "Console output";
}
