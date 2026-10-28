# Proxy Connectivity Test

Standalone .NET 10 console app for checking HTTP, HTTPS, or SOCKS5 proxy
connectivity. SOCKS proxy support is provided by .NET's `SocketsHttpHandler`.

## Run

```sh
dotnet run --project ProxyConnectivityTest
```

The app prompts for a proxy if `PROXY_ADDRESS` is not set. Supported forms:

```text
host:port:username:password
[2001:db8::1]:port:username:password
socks5://proxy.example:1080
socks5://username:password@proxy.example:1080
http://proxy.example:8080
https://proxy.example:8443
```

The provider format `host:port:username:password` is interpreted as an HTTP
proxy. The password may contain `:` characters. IPv6 hosts must be enclosed in
square brackets because colons separate the fields.

For authenticated proxies, prefer entering the URL without credentials and
provide the username/password at the prompts. Password input is hidden.
Credentials can also be passed separately using `PROXY_USERNAME` and
`PROXY_PASSWORD`; URI credentials are supported for compatibility. Do not commit
credentials or share terminal output containing secrets.

The app sends a GET request to `https://api.ipify.org?format=json` by default,
then prints the response containing the observed egress IP. Override the target
with `PROXY_TEST_URL` if needed. Each request asks the server to close the
connection; whether that rotates an IP depends on the proxy provider.

Example using environment variables:

```sh
export PROXY_ADDRESS='socks5://proxy.example:1080'
export PROXY_USERNAME='your-username'
export PROXY_PASSWORD='your-password'
dotnet run --project ProxyConnectivityTest
unset PROXY_ADDRESS PROXY_USERNAME PROXY_PASSWORD
```
