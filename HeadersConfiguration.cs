using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace hcurl
{
    class HeadersConfiguration {

        private readonly List<(Regex pattern, List<(string key, string value)> headers)> headersByHostPattern;

        public HeadersConfiguration(string headersFile) {

            this.headersByHostPattern = new List<(Regex pattern, List<(string key, string value)> headers)>();

            var headerRegex = new Regex(@"^\s+(?<key>[\w-]+)\s*:\s*(?<value>\S.*\S)\s*$", RegexOptions.Compiled);
            List<(string key, string value)>? currentList = null;

            foreach (var line in File.ReadAllLines(headersFile)) {
                var match = headerRegex.Match(line);

                if (match.Success) {

                    var header = (key: match.Groups["key"].Value, value: match.Groups["value"].Value);

                    if (currentList == null) {
                        throw new Exception($"No host pattern found for header {header.key}: {header.value}");
                    }

                    currentList.Add(header);
                }

                else if (!string.IsNullOrWhiteSpace(line)) {
                    currentList = new List<(string key, string value)>();

                    this.headersByHostPattern.Add((new Regex($"^({line.Trim()})$"), currentList));
                }

            }

        }

        public (string key, string value)[] GetHeaders(Uri url) {
            return this.headersByHostPattern
                .Where(hostHeaders => hostHeaders.pattern.IsMatch(url.Host))
                .SelectMany(hostHeaders => hostHeaders.headers)
                .ToArray();
        }

    }

}
