// See https://aka.ms/new-console-template for more information

using hcurl;
using System.Diagnostics;

var headerFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".headers");

var url = args
    .Select(a => Uri.TryCreate(a, UriKind.Absolute, out var url) ? url : null)
    .FirstOrDefault(a => a?.IsFile == false);

var psi = new ProcessStartInfo();

if (url != null && File.Exists(headerFile))
{
    (string key, string value)[] headerList;

    try
    {
        headerList = await new HeadersConfiguration(headerFile).GetHeadersAsync(url);
    }
    catch (HcurlException e)
    {
        Console.Error.WriteLine($"hcurl: {e.Message}");
        Environment.Exit(1);
        return;
    }

    foreach(var header in headerList)
    {
        psi.ArgumentList.Add("-H");
        psi.ArgumentList.Add($"{header.key}: {header.value}");
    }

}

foreach (var item in args)
    psi.ArgumentList.Add(item);

psi.FileName = "curl";
psi.UseShellExecute = false;

var process = Process.Start(psi);

if(process == null)
{
    Environment.Exit(1);
}

process.WaitForExit();

Environment.Exit(process.ExitCode);
