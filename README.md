# hcurl

`hcurl` is a thin wrapper around `curl`. It looks at the URL in the command line, finds the matching entry in `~/.headers`, adds the configured headers (`-H`) to the request, and then runs `curl` with all the original arguments.

Header values can pull secrets from [1Password](https://developer.1password.com/docs/cli/) and can obtain OAuth tokens automatically.

```sh
hcurl https://api.prod.example.com/orders/v1/42
# runs: curl -H "Authorization: Bearer <token>" https://api.prod.example.com/orders/v1/42
```

If there is no `~/.headers` file, or no entry matches, `hcurl` behaves exactly like `curl`. `curl` must be on your `PATH`.

## Installation

Prebuilt binaries for Linux, macOS and Windows (x64 and arm64) are built by the GitHub Actions workflow and attached to each release.

To build it yourself you need the .NET 9 SDK (the project is published with native AOT):

```sh
dotnet publish hcurl.csproj -c Release -r osx-arm64 -o publish   # or linux-x64, win-x64, ...
cp publish/hcurl ~/bin/hcurl                                      # anywhere on your PATH
```

To use the secrets feature, install the [1Password CLI (`op`)](https://developer.1password.com/docs/cli/get-started/) and sign in.

## Configuration

The configuration lives in `~/.headers` (in your home directory). It is a list of entries. Each entry is a line with the match pattern, followed by indented header lines:

```
HOST_REGEX [PATH_REGEX]
    Header-Name: header value
    Another-Header: another value
```

- **`HOST_REGEX`** is a regular expression matched against the whole host name (it is anchored with `^(...)$`). The port is not part of the host.
- **`PATH_REGEX`** is optional. If present, it is separated from the host by whitespace and matched (not anchored, unless you add `^`/`$`) against the URL path, without the query string.
- Header lines must be indented. Blank lines are ignored.
- **Only the first entry that matches is applied.** Put specific entries (with a path) before general ones (host only).

### Example

```
api\.prod\.example\.com ^\/orders\/v1\/
    Authorization: Bearer #token(https://auth.example.com/as/token, #op(Vault/orders-prod/client-id), #op(Vault/orders-prod/client-secret), orders:read orders:write)

api\.prod\.example\.com
    apikey: #op(Vault/apikey-prod/password)

localhost
    X-Debug: true
```

- `https://api.prod.example.com/orders/v1/42` gets the `Authorization` header only.
- `https://api.prod.example.com/other` gets `apikey` only.

Since only the first match applies, an entry that needs both headers must list both.

### Instructions in header values

Header values can contain these instructions, which may be mixed with literal text:

#### `#op(SECRET_PATH)`

Reads a secret using `op read --no-newline op://SECRET_PATH`. For example, `#op(Vault/item/password)` resolves `op://Vault/item/password`. The value is cached in memory for the duration of the `hcurl` run only. It is never written to disk.

#### `#token(URL, CLIENT_ID, CLIENT_SECRET, SCOPE)`

Obtains an OAuth access token with the client credentials grant:

- `POST URL` with `Content-Type: application/x-www-form-urlencoded`
- body: `grant_type=client_credentials&client_id=...&client_secret=...&scope=...`
- the token is the `access_token` field of the JSON response, and it replaces the `#token(...)` text.

Arguments can themselves be `#op(...)` instructions, so credentials never have to be in the file. Scopes are separated by spaces, as usual in OAuth.

Tokens are cached in `~/.hcurl/token-cache.json` (permissions `0600` on macOS/Linux) until shortly before they expire (60 second margin, using the `expires_in` value from the response). While a token is cached, `hcurl` doesn't call the token endpoint and doesn't call `op`. Set `HCURL_NO_CACHE=1` to ignore the cache and always request a new token.

#### Limitations

- Arguments are separated by commas; a literal comma inside an argument is not supported.
- `#op(...)` values are not cached between runs, so every run of `hcurl` that matches an entry with `#op` calls `op` (and may prompt you to unlock 1Password).
- Anything that isn't `#op(` or `#token(` is left as literal text.

### Errors

Problems with an instruction (`op` failing, the token request returning an error, an invalid response) are printed as `hcurl: <message>` and `hcurl` exits with status 1 without running `curl`.

## Using hcurl from Vim and Neovim

Both editors use `curl` through the built-in `netrw` plugin when you open a URL (`:e https://...`, `:Nread`, `vim https://...`). Netrw lets you change the command with two variables:

- `g:netrw_http_cmd`: the program to run (default is `curl`, or `wget` if `curl` isn't found).
- `g:netrw_http_xcmd`: the arguments that go before the destination file and URL. They should end with `-o` so that curl writes to netrw's temporary file.

`hcurl` takes the same arguments as `curl`, so you only need to point netrw at it.

### Vim

Add to your `vimrc` (`~/.vimrc` or `~/.vim/vimrc`):

```vim
let s:hcurl = expand("~/bin/hcurl")   " use ~/bin/hcurl.exe on Windows

if filereadable(s:hcurl)
    let g:netrw_http_cmd = s:hcurl
    let g:netrw_http_xcmd = '-o'
endif
```

If you also have a `~/.curlrc` that you want to keep using, include it explicitly:

```vim
    let g:netrw_http_xcmd = '-K' . expand('~/.curlrc') . ' -o'
```

### Neovim

Neovim ships netrw as an optional package, so load it first. Add to `~/.config/nvim/init.lua` (or a file it requires):

```lua
vim.cmd("packadd! netrw")

local hcurl = vim.fn.expand(vim.fn.has("win32") == 1 and "~/bin/hcurl.exe" or "~/bin/hcurl")

if vim.fn.filereadable(hcurl) == 1 then
  vim.g.netrw_http_cmd = hcurl
  vim.g.netrw_http_xcmd = "-o"

  -- optional: keep using ~/.curlrc
  local curlrc = vim.fn.expand("~/.curlrc")
  if vim.fn.filereadable(curlrc) == 1 then
    vim.g.netrw_http_xcmd = "-K" .. curlrc .. " -o"
  end
end
```

If you use Vim and Neovim with a shared configuration, put the Vim version in the shared `vimrc`; the Neovim snippet is only needed if Neovim doesn't load it.

### Checking that it works

Open a URL that matches an entry in `~/.headers`:

```vim
:e https://api.prod.example.com/orders/v1/42
```

and verify that the content loads (instead of a 401/403). To see the actual command, run `:echo g:netrw_http_cmd` and `:echo g:netrw_http_xcmd`, or temporarily set `let g:netrw_silent = 0` to see the command netrw runs.

### Tips

- If the response is JSON, you can pretty-print it with `:%!jq .` and `:set ft=json`.
- The editor blocks while `hcurl` runs. If the first request needs a token, it may also have to wait for 1Password to unlock; unlock it first (for example with `op signin`) if the editor seems stuck.
