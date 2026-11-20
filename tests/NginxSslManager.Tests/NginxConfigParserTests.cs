using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Tests;

public class NginxConfigParserTests
{
    [Fact]
    public void Tokenize_IgnoresComments_AndKeepsQuotedValues()
    {
        var tokens = NginxConfigParser.Tokenize("""
            # this is a comment
            server_name "example.com" www.example.com;
            """);

        Assert.Contains(tokens, t => t.Kind == NginxTokenKind.Word && t.Value == "example.com" && t.Quoted);
        Assert.Contains(tokens, t => t.Kind == NginxTokenKind.Word && t.Value == "www.example.com");
        Assert.DoesNotContain(tokens, t => t.Value.Contains("comment"));
        Assert.Equal(NginxTokenKind.Semicolon, tokens[^1].Kind);
    }

    [Fact]
    public void Parse_ReadsNestedServerBlockDirectives()
    {
        var tree = NginxConfigParser.Parse("""
            http {
                server {
                    listen 80 default_server;
                    listen 443 ssl;
                    server_name example.com www.example.com;
                    ssl_certificate /etc/nginx/ssl/example.com/fullchain.pem;
                    ssl_certificate_key /etc/nginx/ssl/example.com/privkey.pem;

                    location /.well-known/acme-challenge/ {
                        root /var/www/letsencrypt;
                    }
                }
            }
            """, "test.conf");

        var http = Assert.Single(tree);
        var server = Assert.Single(http.Direct("server"));

        var listens = server.Direct("listen").ToList();
        Assert.Equal(2, listens.Count);
        Assert.Equal(["80", "default_server"], listens[0].Args);
        Assert.Equal(["443", "ssl"], listens[1].Args);

        var serverName = Assert.Single(server.Direct("server_name"));
        Assert.Equal(["example.com", "www.example.com"], serverName.Args);

        Assert.Equal(
            "/etc/nginx/ssl/example.com/fullchain.pem",
            Assert.Single(server.Direct("ssl_certificate")).FirstArg);

        var location = Assert.Single(server.Direct("location"));
        Assert.Equal("/.well-known/acme-challenge/", location.FirstArg);
    }

    [Fact]
    public void Parse_KeepsLineNumbers_AndBraceOffsets()
    {
        var text = "http {\n  server {\n    listen 443 ssl;\n  }\n}\n";
        var http = Assert.Single(NginxConfigParser.Parse(text, "a.conf"));
        var server = Assert.Single(http.Direct("server"));

        Assert.Equal(1, http.Line);
        Assert.Equal(2, server.Line);
        Assert.Equal(text.IndexOf("server {", StringComparison.Ordinal) + "server ".Length, server.OpenBraceOffset);
    }

    [Fact]
    public void Parse_IsForgivingWithUnclosedBlocks()
    {
        // nginx 解析器故意保持宽容：截断的配置仍会返回已解析的部分，
        // 由后续的 nginx -t 校验负责给出真正的错误。
        var tree = NginxConfigParser.Parse("http { server { listen 80; ", "broken.conf");

        var http = Assert.Single(tree);
        var server = Assert.Single(http.Direct("server"));
        Assert.Equal(["80"], Assert.Single(server.Direct("listen")).Args);
    }
}
