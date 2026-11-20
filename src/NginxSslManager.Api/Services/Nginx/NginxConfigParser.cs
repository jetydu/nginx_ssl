namespace NginxSslManager.Api.Services.Nginx;

public enum NginxTokenKind
{
    Word,
    OpenBrace,
    CloseBrace,
    Semicolon
}

public sealed class NginxToken
{
    public NginxTokenKind Kind { get; init; }
    public string Value { get; init; } = "";
    public int Line { get; init; }
    public int Start { get; init; }
    public bool Quoted { get; init; }
}

public sealed class NginxDirective
{
    public string Name { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public List<NginxDirective>? Children { get; set; }
    public bool IsBlock => Children is not null;
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int OpenBraceOffset { get; set; }

    public string? FirstArg => Args.Count > 0 ? Args[0] : null;

    public IEnumerable<NginxDirective> Direct(string name) =>
        (Children ?? []).Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A forgiving tokenizer/parser for the nginx configuration syntax. It keeps line
/// numbers and source offsets so the manager can safely edit single directives.
/// </summary>
public static class NginxConfigParser
{
    public static List<NginxToken> Tokenize(string text)
    {
        var tokens = new List<NginxToken>(Math.Max(16, text.Length / 8));
        var i = 0;
        var line = 1;

        while (i < text.Length)
        {
            var ch = text[i];

            if (ch == '\n')
            {
                line++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch == '#')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (ch == '{' || ch == '}' || ch == ';')
            {
                tokens.Add(new NginxToken
                {
                    Kind = ch switch
                    {
                        '{' => NginxTokenKind.OpenBrace,
                        '}' => NginxTokenKind.CloseBrace,
                        _ => NginxTokenKind.Semicolon
                    },
                    Value = ch.ToString(),
                    Line = line,
                    Start = i
                });
                i++;
                continue;
            }

            var start = i;
            var tokenLine = line;
            var quoted = ch is '"' or '\'';
            var quote = quoted ? ch : '\0';
            var value = new System.Text.StringBuilder();

            if (quoted)
            {
                i++;
                while (i < text.Length)
                {
                    var c = text[i];
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        value.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (c == quote)
                    {
                        i++;
                        break;
                    }

                    if (c == '\n')
                    {
                        line++;
                    }

                    value.Append(c);
                    i++;
                }
            }
            else
            {
                while (i < text.Length)
                {
                    var c = text[i];

                    if (char.IsWhiteSpace(c) || c is ';' or '{' or '}' or '#')
                    {
                        break;
                    }

                    if (c == '\\' && i + 1 < text.Length)
                    {
                        value.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    // nginx variables can use ${name}; keep the braces inside the word.
                    if (c == '$' && i + 1 < text.Length && text[i + 1] == '{')
                    {
                        var close = text.IndexOf('}', i + 2);
                        if (close > 0)
                        {
                            value.Append(text, i, close - i + 1);
                            i = close + 1;
                            continue;
                        }
                    }

                    value.Append(c);
                    i++;
                }
            }

            tokens.Add(new NginxToken
            {
                Kind = NginxTokenKind.Word,
                Value = value.ToString(),
                Line = tokenLine,
                Start = start,
                Quoted = quoted
            });
        }

        return tokens;
    }

    public static List<NginxDirective> Parse(string text, string file)
    {
        var tokens = Tokenize(text);
        var index = 0;
        return ParseBlock(tokens, ref index, file, expectClose: false);
    }

    private static List<NginxDirective> ParseBlock(
        List<NginxToken> tokens,
        ref int index,
        string file,
        bool expectClose)
    {
        var result = new List<NginxDirective>();

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (token.Kind == NginxTokenKind.CloseBrace)
            {
                index++;
                if (expectClose)
                {
                    return result;
                }

                continue;
            }

            if (token.Kind != NginxTokenKind.Word)
            {
                index++;
                continue;
            }

            var directive = new NginxDirective
            {
                Name = token.Value,
                File = file,
                Line = token.Line
            };
            index++;

            while (index < tokens.Count && tokens[index].Kind == NginxTokenKind.Word)
            {
                directive.Args.Add(tokens[index].Value);
                index++;
            }

            if (index < tokens.Count && tokens[index].Kind == NginxTokenKind.OpenBrace)
            {
                directive.OpenBraceOffset = tokens[index].Start;
                directive.Children = [];
                index++;
                directive.Children = ParseBlock(tokens, ref index, file, expectClose: true);
            }
            else if (index < tokens.Count && tokens[index].Kind == NginxTokenKind.Semicolon)
            {
                index++;
            }

            result.Add(directive);
        }

        return result;
    }
}
