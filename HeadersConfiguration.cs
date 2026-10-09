using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace hcurl
{
    class HeadersConfiguration {

        private readonly List<(Regex hostPattern, Regex? pathPattern, List<(string key, ValueExpression value)> headers)> headersByPattern;

        public HeadersConfiguration(string headersFile) {

            this.headersByPattern = new List<(Regex hostPattern, Regex? pathPattern, List<(string key, ValueExpression value)> headers)>();

            var headerRegex = new Regex(@"^\s+(?<key>[\w-]+)\s*:\s*(?<value>\S.*\S)\s*$", RegexOptions.Compiled);
            var patternRegex = new Regex(@"^(?<host>\S+)(?:\s+(?<path>\S.*))?$", RegexOptions.Compiled);
            List<(string key, ValueExpression value)>? currentList = null;

            foreach (var line in File.ReadAllLines(headersFile)) {
                var match = headerRegex.Match(line);

                if (match.Success) {

                    var key = match.Groups["key"].Value;
                    var value = match.Groups["value"].Value;

                    if (currentList == null) {
                        throw new HcurlException($"No host pattern found for header {key}: {value}");
                    }

                    currentList.Add((key, ValueExpression.Parse(value)));
                }

                else if (!string.IsNullOrWhiteSpace(line)) {
                    currentList = new List<(string key, ValueExpression value)>();

                    var patterns = patternRegex.Match(line.Trim());
                    var hostPattern = new Regex($"^({patterns.Groups["host"].Value})$");
                    var pathPattern = patterns.Groups["path"].Success ? new Regex(patterns.Groups["path"].Value) : null;

                    this.headersByPattern.Add((hostPattern, pathPattern, currentList));
                }

            }

        }

        public async Task<(string key, string value)[]> GetHeadersAsync(Uri url) {
            var resolvers = new Resolvers();
            var result = new List<(string key, string value)>();

            var headers = this.headersByPattern
                .Where(entry => entry.hostPattern.IsMatch(url.Host))
                .Where(entry => entry.pathPattern == null || entry.pathPattern.IsMatch(url.AbsolutePath))
                .SelectMany(entry => entry.headers);

            foreach (var header in headers)
                result.Add((header.key, await header.value.EvaluateAsync(resolvers)));

            return result.ToArray();
        }

    }

}
