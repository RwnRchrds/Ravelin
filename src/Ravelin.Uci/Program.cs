using Ravelin.Uci;

// GUIs and cutechess-cli read the engine line by line, so responses must not sit in a buffer.
var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Console.SetOut(output);

new UciEngine(output).Run(Console.In);
