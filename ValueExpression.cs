using System.Text;

namespace hcurl
{
    class Resolvers {
        public OpSecretResolver Op { get; } = new OpSecretResolver();
        public TokenResolver Token { get; } = new TokenResolver();
    }

    // Header value, possibly containing #op(...) and #token(...) instructions.
    // Arguments are separated by commas; literal commas inside arguments are not supported.
    abstract class ValueExpression {

        public abstract Task<string> EvaluateAsync(Resolvers resolvers);

        public static ValueExpression Parse(string text) {
            var parser = new Parser(text);
            return parser.ParseValue();
        }

        class Literal : ValueExpression {
            public string Text { get; set; }
            public Literal(string text) { this.Text = text; }
            public override Task<string> EvaluateAsync(Resolvers resolvers) => Task.FromResult(this.Text);
        }

        class Concat : ValueExpression {
            private readonly List<ValueExpression> parts;
            public Concat(List<ValueExpression> parts) { this.parts = parts; }

            public override async Task<string> EvaluateAsync(Resolvers resolvers) {
                var result = new StringBuilder();
                foreach (var part in this.parts)
                    result.Append(await part.EvaluateAsync(resolvers));
                return result.ToString();
            }
        }

        class Call : ValueExpression {
            private readonly string name;
            private readonly List<ValueExpression> args;
            private readonly string rawText;

            public Call(string name, List<ValueExpression> args, string rawText) {
                this.name = name;
                this.args = args;
                this.rawText = rawText;
            }

            public override async Task<string> EvaluateAsync(Resolvers resolvers) {
                switch (this.name) {
                    case "op":
                        return await resolvers.Op.ReadAsync(await this.args[0].EvaluateAsync(resolvers));

                    case "token":
                        return await resolvers.Token.GetTokenAsync(this.rawText, async () => (
                            url: await this.args[0].EvaluateAsync(resolvers),
                            clientId: await this.args[1].EvaluateAsync(resolvers),
                            clientSecret: await this.args[2].EvaluateAsync(resolvers),
                            scope: await this.args[3].EvaluateAsync(resolvers)));

                    default:
                        throw new InvalidOperationException($"Unknown instruction #{this.name}");
                }
            }
        }

        class Parser {
            private static readonly Dictionary<string, int> functionArity = new Dictionary<string, int> {
                ["op"] = 1,
                ["token"] = 4,
            };

            private readonly string text;
            private int pos;

            public Parser(string text) { this.text = text; }

            public ValueExpression ParseValue() => this.ParseParts(insideArgument: false);

            private ValueExpression ParseParts(bool insideArgument) {
                var parts = new List<ValueExpression>();
                var literal = new StringBuilder();
                var depth = 0;

                while (this.pos < this.text.Length) {
                    var c = this.text[this.pos];

                    if (c == '#' && this.TryMatchFunction(out var name)) {
                        if (literal.Length > 0) {
                            parts.Add(new Literal(literal.ToString()));
                            literal.Clear();
                        }
                        parts.Add(this.ParseCall(name));
                        continue;
                    }

                    if (insideArgument && depth == 0 && (c == ',' || c == ')'))
                        break;

                    if (c == '(') depth++;
                    else if (c == ')' && depth > 0) depth--;

                    literal.Append(c);
                    this.pos++;
                }

                if (literal.Length > 0)
                    parts.Add(new Literal(literal.ToString()));

                if (insideArgument) {
                    if (parts.FirstOrDefault() is Literal first) first.Text = first.Text.TrimStart();
                    if (parts.LastOrDefault() is Literal last) last.Text = last.Text.TrimEnd();
                }

                return parts.Count == 1 ? parts[0] : new Concat(parts);
            }

            private bool TryMatchFunction(out string name) {
                foreach (var candidate in functionArity.Keys) {
                    if (string.CompareOrdinal(this.text, this.pos + 1, candidate + "(", 0, candidate.Length + 1) == 0) {
                        name = candidate;
                        return true;
                    }
                }
                name = "";
                return false;
            }

            private ValueExpression ParseCall(string name) {
                var start = this.pos;
                this.pos += name.Length + 2; // '#', name, '('

                var args = new List<ValueExpression>();

                while (true) {
                    args.Add(this.ParseParts(insideArgument: true));

                    if (this.pos >= this.text.Length)
                        throw new HcurlException($"Missing ')' for #{name}(...) in: {this.text}");

                    var delimiter = this.text[this.pos++];
                    if (delimiter == ')')
                        break;
                }

                if (args.Count != functionArity[name])
                    throw new HcurlException($"#{name}(...) expects {functionArity[name]} argument(s) but got {args.Count} in: {this.text}");

                return new Call(name, args, this.text[start..this.pos]);
            }
        }
    }
}
