using System.ComponentModel;
using System.Diagnostics;

namespace hcurl
{
    // Reads secrets from 1Password using the `op` CLI.
    class OpSecretResolver {

        private readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        public async Task<string> ReadAsync(string secretPath) {
            if (this.cache.TryGetValue(secretPath, out var cached))
                return cached;

            var reference = $"op://{secretPath}";

            var psi = new ProcessStartInfo {
                FileName = "op",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("read");
            psi.ArgumentList.Add("--no-newline");
            psi.ArgumentList.Add(reference);

            Process? process;
            try {
                process = Process.Start(psi);
            }
            catch (Win32Exception e) {
                throw new HcurlException($"Unable to run 1Password CLI 'op': {e.Message}");
            }

            if (process == null)
                throw new HcurlException("Unable to run 1Password CLI 'op'");

            using (process) {
                var value = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    throw new HcurlException($"op read failed for {reference} (exit code {process.ExitCode})");

                this.cache[secretPath] = value;
                return value;
            }
        }

    }
}
