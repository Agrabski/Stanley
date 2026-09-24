using System.CommandLine;
using Stanley.Cli.Commands;

var root = new RootCommand("Stanley - a comic project toolkit.");
root.Add(InitCommand.Build());

var parseResult = root.Parse(args);
return parseResult.Invoke();
